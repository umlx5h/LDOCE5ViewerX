using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

public sealed class DictionaryContentServiceTests
{
    [Fact]
    public void LoadPlainText_loads_fs_item_from_test_filemap_and_archive()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem("fs", "entry", "<Entry><Head><HWD><BASE>welcome</BASE></HWD></Head><Sense><DEF>Hello world.</DEF></Sense></Entry>");
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        string text = service.LoadPlainText("/fs/entry");

        text.Should().Contain("welcome");
        text.Should().Contain("Hello world.");
    }

#if DEBUG
    [Fact]
    public void LoadRawXml_loads_fs_item_as_indented_xml_string()
    {
        using TestDictionaryFixture fixture = new();
        string xml = "<Entry><Head><HWD><BASE>welcome</BASE></HWD></Head><Sense><DEF>Hello world.</DEF></Sense></Entry>";
        fixture.AddArchiveItem("fs", "entry", xml);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        string rawXml = service.LoadRawXml("/fs/entry#sense");

        rawXml.Should().Contain("<Entry>");
        rawXml.Should().Contain("  <Head>");
        rawXml.Should().Contain("    <HWD>");
        rawXml.Should().Contain("      <BASE>welcome</BASE>");
    }

    [Fact]
    public void LoadRawXml_loads_both_activator_xml_items()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem("activator_concept", "concept", "<Concept><HWD>concept</HWD></Concept>");
        fixture.AddArchiveItem("activator_section", "section", "<Section><Exponent><EXP>section</EXP></Exponent></Section>");
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        string rawXml = service.LoadRawXml("/activator/concept/section");

        rawXml.Should().Contain("<Concept>");
        rawXml.Should().Contain("  <HWD>concept</HWD>");
        rawXml.Should().Contain("<Section>");
        rawXml.Should().Contain("  <Exponent>");
        rawXml.Should().Contain("    <EXP>section</EXP>");
    }
#endif

    [Fact]
    public void LoadContent_loads_rich_fs_item_with_links_audio_and_images()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head>
                <HWD><BASE>welcome</BASE></HWD>
                <POS>verb</POS>
                <Audio resource="GB_HWD_PRON" topic="gb_hwd_pron/welcome.mp3"/>
              </Head>
              <Sense id="idm.dict.abc.sense">
                <DEF>Hello <Ref topic="idm.dict.abc.other" bookmark="idm.dict.abc.target">world</Ref>.</DEF>
                <ILLUSTRATION thumb="picture/thumbnail/welcome.jpg"/>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.sense");

        page.Title.Should().Contain("welcome");
        page.Anchor.Should().Be("abc.sense");
        Flatten(page.Document).Should().Contain("welcome");
        Flatten(page.Document).Should().Contain("Hello");
        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryAudioInline>()
            .Should()
            .ContainSingle(audio => audio.Resource.Archive == "gb_hwd_pron" && audio.Resource.Name == "welcome.mp3");
        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Should()
            .ContainSingle(link => link.Target.Value == "/fs/abc.other#abc.target");
        DictionaryImageBlock image = AllBlocks(page.Document)
            .OfType<DictionaryImageBlock>()
            .Should()
            .ContainSingle(image => image.Resource.Name == "thumbnail/welcome.jpg")
            .Subject;
        image.Target.Should().NotBeNull();
        image.Target!.Kind.Should().Be(DictionaryLinkTargetKind.Image);
        image.Target.Value.Should().Be("/picture/fullsize/welcome.jpg");
    }

    [Theory]
    [InlineData("GB_HWD_PRON", "gb_hwd_pron/welcome.mp3", "gb_hwd_pron", "Play British pronunciation")]
    [InlineData("US_HWD_PRON", "us_hwd_pron/welcome.mp3", "us_hwd_pron", "Play American pronunciation")]
    [InlineData("EXA_PRON", "exa_pron/example.mp3", "exa_pron", "Play example audio")]
    public void LoadContent_maps_audio_titles_for_icon_tooltips(
        string resource,
        string topic,
        string expectedArchive,
        string expectedTitle)
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            $"""
            <Entry>
              <Head><HWD><BASE>welcome</BASE></HWD></Head>
              <Sense>
                <EXAMPLE><Audio resource="{resource}" topic="{topic}"/><BASE>Example text.</BASE></EXAMPLE>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryAudioInline>()
            .Should()
            .ContainSingle(audio =>
                audio.Resource.Archive == expectedArchive
                && audio.Title == expectedTitle);
    }

    [Fact]
    public void LoadContent_maps_frequency_labels_to_frequency_tag_style()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head>
                <HWD><BASE>make</BASE></HWD>
                <FREQ>S1</FREQ>
                <FREQ>W1</FREQ>
                <POS>verb</POS>
              </Head>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Where(text => text.Style == DictionaryTextStyle.FrequencyTag)
            .Select(text => text.Text)
            .Should()
            .Equal("S1", "W1");
    }

    [Fact]
    public void LoadContent_uses_hyphenation_in_entry_head_without_duplicate_base_headword()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head>
                <HWD><BASE>hello</BASE></HWD>
                <HYPHENATION>hel.lo</HYPHENATION>
                <HOMNUM>1</HOMNUM>
                <FREQ>S1</FREQ>
              </Head>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryParagraphBlock entryHead = page.Document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.EntryHead)
            .Subject;

        ConcatenateInlineText(entryHead.Inlines).Should().Be("hel.lo1 S1");
        entryHead.Inlines
            .OfType<DictionaryTextInline>()
            .Where(text => text.Text == "hel.lo")
            .Should()
            .ContainSingle()
            .Which
            .Style
            .Should()
            .Be(DictionaryTextStyle.Hyphenation);
        entryHead.Inlines
            .OfType<DictionaryTextInline>()
            .Where(text => text.Text == "1")
            .Should()
            .ContainSingle()
            .Which
            .Style
            .Should()
            .Be(DictionaryTextStyle.HomonymNumber);
    }

    [Fact]
    public void RenderEtymologies_formats_head_labels_origin_and_quotes()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderEtymologies(
            Encoding.UTF8.GetBytes(
                """
                <Entry id="12754" idm_id="000012732">
                  <HEAD>
                    <HWD>test</HWD>
                    <HOMNUM>1</HOMNUM>
                  </HEAD>
                  <SENSE>
                    <br />
                    <span class="lead">Date: </span>
                    <CENTURY>1300-1400</CENTURY>
                    <br />
                    <span class="lead">Language: </span>
                    <LANG>Old French</LANG>
                    <br />
                    <span class="lead">Origin: </span>
                    <TRAN><span></span><span class="normal">‘</span>pot for testing metals<span class="normal">’</span></TRAN>
                    <Z>, from </Z>
                    <LANG><span />Latin</LANG>
                    <Z></Z>
                    <ORIGIN><span />testum</ORIGIN>
                    <Z></Z>
                    <TRAN><span class="normal">‘</span>clay pot<span class="normal">’</span></TRAN>
                  </SENSE>
                </Entry>
                """));

        DictionaryParagraphBlock head = document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.EntryHead)
            .Subject;
        head.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "test" && text.Style == DictionaryTextStyle.Hyphenation)
            .And
            .ContainSingle(text => text.Text == "1" && text.Style == DictionaryTextStyle.HomonymNumber);

        DictionaryParagraphBlock sense = AllBlocks(document)
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => ConcatenateInlineText(block.Inlines).Contains("Origin:", StringComparison.Ordinal))
            .Subject;
        ConcatenateInlineText(sense.Inlines).Should().Contain("Origin: ‘pot for testing metals’, from Latin testum ‘clay pot’");
        sense.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .Contain(text => text.Text == "Date: " && text.Style == DictionaryTextStyle.Emphasis)
            .And
            .Contain(text => text.Text == "Language: " && text.Style == DictionaryTextStyle.Emphasis)
            .And
            .Contain(text => text.Text == "Origin: " && text.Style == DictionaryTextStyle.Emphasis)
            .And
            .ContainSingle(text => text.Text.Trim() == "testum" && text.Style == DictionaryTextStyle.Origin);
    }

    [Fact]
    public void LoadContent_formats_entry_head_spacing_around_pronunciation_variants_and_grammar()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head>
                <HWD><BASE>hello</BASE></HWD>
                <HYPHENATION>hel‧lo</HYPHENATION>
                <FREQ>S1</FREQ>
                <span> /</span><PRON>həˈləʊ, he- $ -ˈloʊ</PRON><span>/</span>
                <span>(</span><Variant>also <ORTHVAR>hallo</ORTHVAR>, <ORTHVAR>hullo</ORTHVAR> <GEO>British English</GEO></Variant><span>)</span>
                <POS>interjection</POS>, <POS>noun</POS><span>[</span><GRAM>countable</GRAM><span>]</span>
              </Head>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryParagraphBlock entryHead = page.Document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.EntryHead)
            .Subject;

        ConcatenateInlineText(entryHead.Inlines).Should()
            .Be("hel·lo S1 /həˈləʊ, he- $ -ˈloʊ/ (also hallo, hullo British English) interjection, noun [countable]");
    }

    [Fact]
    public void LoadContent_preserves_tail_text_once()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            "<Entry><Head><HWD><BASE>tail</BASE></HWD></Head><Sense><DEF>one <LEXUNIT>two</LEXUNIT> three</DEF></Sense></Entry>");
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        Flatten(page.Document).Should().Contain("one two three");
        Flatten(page.Document).Should().NotContain("three three");
    }

    [Theory]
    [InlineData("<SIGNPOST>top of body</SIGNPOST>")]
    public void LoadContent_maps_signpost_to_uppercase_tag_style(string signpostXml)
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            $"""
            <Entry>
              <Head><HWD><BASE>head</BASE></HWD></Head>
              <Sense>{signpostXml}<DEF>the top part of your body</DEF></Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryTextInline signpost = page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Style == DictionaryTextStyle.Signpost)
            .Subject;

        signpost.Text.Should().Be("TOP OF BODY");
    }

    [Theory]
    [InlineData("syn", "SYN")]
    [InlineData("opp", "OPP")]
    public void LoadContent_maps_synonym_and_opposite_markers_to_relation_tag_style(string markerText, string expected)
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            $"""
            <Entry>
              <Head><HWD><BASE>small</BASE></HWD></Head>
              <Sense>
                <DEF>not large</DEF>
                <SYN><span class="synopp">{markerText}</span><NonDV><REFHWD>lower case</REFHWD></NonDV></SYN>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryTextInline relationTag = page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Style == DictionaryTextStyle.RelationTag)
            .Subject;

        relationTag.Text.Should().Be(expected);
        Flatten(page.Document).Should().Contain("lower case");
    }

    [Fact]
    public void LoadContent_maps_inline_descendant_anchor_to_inline_text()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head><HWD><BASE>hell</BASE></HWD></Head>
              <Sense id="idm.dict.abc.sense">
                <EXAMPLE id="idm.dict.abc.example">
                  <BASE>He says his time in jail was <COLLOINEXA id="idm.dict.abc.collocation">hell on earth</COLLOINEXA>.</BASE>
                </EXAMPLE>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.collocation");
        DictionaryTextInline inline = page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Anchor == "abc.collocation")
            .Subject;

        page.Anchor.Should().Be("abc.collocation");
        inline.Text.Should().Be("hell on earth");
        inline.Style.Should().Be(DictionaryTextStyle.ExampleStrong);
        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Where(text => text.Text.Contains("He says his time", StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Which.Style.Should().Be(DictionaryTextStyle.Example);
    }

    [Fact]
    public void LoadContent_maps_example_anchor_to_displayed_example_block()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head><HWD><BASE>hell</BASE></HWD></Head>
              <Sense id="idm.dict.abc.sense">
                <EXAMPLE id="idm.dict.abc.example"><BASE>He says his time in jail was hell on earth.</BASE></EXAMPLE>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.example");
        DictionaryBlock example = AllBlocks(page.Document)
            .Should()
            .ContainSingle(block => string.Equals(block.Anchor, "abc.example", StringComparison.Ordinal))
            .Subject;

        page.Anchor.Should().Be("abc.example");
        FlattenBlock(example).Should().Contain("hell on earth");
    }

    [Fact]
    public void LoadContent_maps_collocate_anchor_to_title_block_not_whole_box()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head><HWD><BASE>chemistry</BASE></HWD></Head>
              <Sense>
                <ColloBox>
                  <Collocate id="idm.dict.abc.collocate">
                    <COLLOC>chemistry between</COLLOC>
                    <COLLEXA><BASE>There is real chemistry between them.</BASE></COLLEXA>
                  </Collocate>
                </ColloBox>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.collocate");
        DictionaryBlock highlighted = AllBlocks(page.Document)
            .Should()
            .ContainSingle(block =>
                string.Equals(block.Anchor, "abc.collocate", StringComparison.Ordinal)
                || block.AnchorAliases.Contains("abc.collocate", StringComparer.Ordinal))
            .Subject;

        page.Anchor.Should().Be("abc.collocate");
        FlattenBlock(highlighted).Should().Contain("chemistry between");
        FlattenBlock(highlighted).Should().NotContain("There is real chemistry between them.");
    }

    [Fact]
    public void LoadContent_keeps_direct_collocate_term_and_variant_in_same_block()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test</BASE></HWD></Head>
              <Sense>
                <ColloBox>
                  <Collocate>
                    <COLLOC>take a test</COLLOC>
                    <Variant>
                      <span> (</span>
                      <LINKWORD>also</LINKWORD>
                      <LEXVAR>do/sit a test</LEXVAR>
                      <GEO>British English</GEO>
                      <span>)</span>
                    </Variant>
                    <COLLEXA><BASE>All candidates have to take a test.</BASE></COLLEXA>
                  </Collocate>
                </ColloBox>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock collocateBlock = AllBlocks(page.Document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 2
                && ConcatenateBlockText(block.Blocks[0]).Contains("take a test (also do/sit a test British English)", StringComparison.Ordinal))
            .Subject;

        collocateBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        collocateBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(collocateBlock.Blocks[1]).Should().Be("\u2022 All candidates have to take a test.");
    }

    [Fact]
    public void LoadContent_maps_propformprep_anchor_to_inline_text_not_whole_example()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head><HWD><BASE>chemistry</BASE></HWD></Head>
              <Sense>
                <GramExa>
                  <PROPFORMPREP id="idm.dict.abc.phrase">chemistry between</PROPFORMPREP>
                  <EXAMPLE><BASE>It is obvious that there is real chemistry between them.</BASE></EXAMPLE>
                </GramExa>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.phrase");
        DictionaryTextInline inline = page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Anchor == "abc.phrase")
            .Subject;
        IEnumerable<DictionaryBlock> highlightedBlocks = AllBlocks(page.Document)
            .Where(block =>
                string.Equals(block.Anchor, "abc.phrase", StringComparison.Ordinal)
                || block.AnchorAliases.Contains("abc.phrase", StringComparer.Ordinal));

        page.Anchor.Should().Be("abc.phrase");
        inline.Text.Should().Be("chemistry between");
        highlightedBlocks.Should().BeEmpty();
    }

    [Fact]
    public void LoadContent_propagates_inline_anchor_to_nested_markup_text()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry id="idm.dict.abc.entry">
              <Head><HWD><BASE>chemistry</BASE></HWD></Head>
              <Sense>
                <GramExa>
                  <PROPFORMPREP id="idm.dict.abc.phrase"><span>chemistry between</span></PROPFORMPREP>
                  <EXAMPLE><BASE>It is obvious that there is real chemistry between them.</BASE></EXAMPLE>
                </GramExa>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry#abc.phrase");
        DictionaryTextInline inline = page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Anchor == "abc.phrase")
            .Subject;

        inline.Text.Should().Be("chemistry between");
    }

    [Fact]
    public void LoadContent_splits_grammar_label_and_examples_into_separate_blocks()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test</BASE></HWD></Head>
              <Sense>
                <GramExa>
                  <PROPFORM>test on</PROPFORM>
                  <EXAMPLE><BASE>We have a test on irregular verbs tomorrow.</BASE></EXAMPLE>
                  <EXAMPLE><BASE>Did you get a good mark in the test?</BASE></EXAMPLE>
                </GramExa>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock grammarBlock = AllBlocks(page.Document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 3
                && FlattenBlock(block.Blocks[0]) == "test on")
            .Subject;

        grammarBlock.Blocks.Should().HaveCount(3);
        grammarBlock.Blocks[0].Should().BeOfType<DictionaryParagraphBlock>();
        grammarBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        FlattenBlock(grammarBlock.Blocks[0]).Should().Be("test on");
        grammarBlock.Blocks.Skip(1).Should().AllSatisfy(block => block.Style.Should().Be(DictionaryBlockStyle.Example));
    }

    [Fact]
    public void LoadContent_splits_collocation_label_and_example_into_separate_blocks()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test</BASE></HWD></Head>
              <Sense>
                <ColloExa>
                  <COLLO>do/run a test</COLLO>
                  <EXAMPLE>
                    <Audio resource="EXA_PRON" topic="exa_pron/test.mp3"/>
                    <BASE>They don't know what's wrong with her yet - they're doing tests.</BASE>
                  </EXAMPLE>
                </ColloExa>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock collocationBlock = AllBlocks(page.Document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 2
                && FlattenBlock(block.Blocks[0]) == "do/run a test")
            .Subject;

        collocationBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        collocationBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        FlattenBlock(collocationBlock.Blocks[1]).Should().Contain("They don't know");
        collocationBlock.Blocks[1].Should().NotBeSameAs(collocationBlock.Blocks[0]);
    }

    [Fact]
    public void RenderCollocations_splits_wrapped_collocate_head_and_body_into_separate_blocks()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderCollocations(
            Encoding.UTF8.GetBytes(
                """
                <Collos>
                  <ColloBox>
                    <Section>
                      <Collocate fold="yes">
                        <coll-head>
                          <COLLOC>As far as I can make out</COLLOC>
                        </coll-head>
                        <coll-body hide="yes">
                          <COLLEXA>
                            <span class="exabullet">‧ </span>
                            <b>As far as I can make out</b>, he has never been married.
                          </COLLEXA>
                        </coll-body>
                      </Collocate>
                    </Section>
                  </ColloBox>
                </Collos>
                """));

        DictionaryContainerBlock collocateBlock = AllBlocks(document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 2
                && FlattenBlock(block.Blocks[0]) == "As far as I can make out")
            .Subject;

        collocateBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        collocateBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(collocateBlock.Blocks[1]).Should().Contain("As far as I can make out, he has never been married.");
        collocateBlock.Blocks[1].Should().NotBeSameAs(collocateBlock.Blocks[0]);
    }

    [Fact]
    public void RenderCollocations_keeps_wrapped_collocate_head_and_leading_variant_in_same_block()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderCollocations(
            Encoding.UTF8.GetBytes(
                """
                <Collos>
                  <ColloBox>
                    <Section>
                      <Collocate fold="yes">
                        <coll-head>
                          <COLLOC>take a test</COLLOC>
                        </coll-head>
                        <coll-body hide="yes">
                          <Variant>
                            <span class="neutral"> (</span>
                            <LINKWORD>also</LINKWORD>
                            <LEXVAR> do/sit a test</LEXVAR>
                            <GEO> British English</GEO>
                            <span class="neutral">)</span>
                          </Variant>
                          <COLLEXA><span class="exabullet">‧ </span>All candidates have to take a test.</COLLEXA>
                        </coll-body>
                      </Collocate>
                    </Section>
                  </ColloBox>
                </Collos>
                """));

        DictionaryContainerBlock collocateBlock = AllBlocks(document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 2
                && ConcatenateBlockText(block.Blocks[0]).Contains("take a test (also do/sit a test British English)", StringComparison.Ordinal))
            .Subject;

        collocateBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        collocateBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(collocateBlock.Blocks[1]).Should().Be("\u2022 All candidates have to take a test.");
    }

    [Fact]
    public void RenderCollocations_maps_bold_text_inside_collocation_examples()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderCollocations(
            Encoding.UTF8.GetBytes(
                """
                <Collos>
                  <ColloBox>
                    <Section>
                      <Collocate fold="yes">
                        <coll-head>
                          <COLLOC>made a dart for</COLLOC>
                        </coll-head>
                        <coll-body hide="yes">
                          <COLLEXA>
                            <span class="exabullet">‧ </span>The prisoner <b>made a dart for</b> the door.
                          </COLLEXA>
                        </coll-body>
                      </Collocate>
                    </Section>
                  </ColloBox>
                </Collos>
                """));

        document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "made a dart for" && text.Style == DictionaryTextStyle.ExampleStrong);
    }

    [Fact]
    public void RenderPhrases_keeps_examples_italic_and_maps_bold_text()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderPhrases(
            Encoding.UTF8.GetBytes(
                """
                <Phrases>
                  <phrase fold="yes">
                    <phrase-head>
                      <Ref resource="LdoceAZ" topic="u2fc098491a42200a.6e2b450a.1150446158e.5348">put sb/sth to the test</Ref>
                    </phrase-head>
                    <phrase-body hide="yes">
                      <exa><span class="exabullet">‧ </span>Kathy's students are putting her patience to the <b>test</b>.</exa>
                    </phrase-body>
                  </phrase>
                </Phrases>
                """));

        DictionaryParagraphBlock example = document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => ConcatenateBlockText(block).Contains("Kathy's students", StringComparison.Ordinal))
            .Subject;

        example.Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(example).Should().Be("\u2022 Kathy's students are putting her patience to the test.");
        example.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "test" && text.Style == DictionaryTextStyle.ExampleStrong);
    }

    [Fact]
    public void RenderExamples_maps_bold_text_inside_examples()
    {
        (string title, DictionaryDocument document) = RichContentRenderer.RenderExamples(
            Encoding.UTF8.GetBytes(
                """
                <examples>
                  <exa-head>
                    <hwd>test</hwd>
                    <pos>n</pos>
                  </exa-head>
                  <exa-body>
                    <exa><span class="exabullet">‧ </span>a ban on nuclear <b>tests</b></exa>
                  </exa-body>
                </examples>
                """));

        title.Should().Be("test");
        DictionaryParagraphBlock example = document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => ConcatenateBlockText(block).Contains("a ban on nuclear tests", StringComparison.Ordinal))
            .Subject;

        example.Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(example).Should().Be("\u2022 a ban on nuclear tests");
        example.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "tests" && text.Style == DictionaryTextStyle.ExampleStrong);
    }

    [Fact]
    public void RenderThesaurus_keeps_wrapped_exponent_head_and_definition_in_same_block()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderThesaurus(
        [
            Encoding.UTF8.GetBytes(
                """
                <Section>
                  <Exponent fold="yes">
                    <exp-head>
                      <EXP>hey</EXP>
                    </exp-head>
                    <exp-body hide="yes">
                      <LABEL>especially AmE</LABEL>
                      <LABEL> informal</LABEL>
                      <DEF> used as a friendly greeting</DEF>
                      <THESEXA><span class="exabullet">‧ </span>Hey, Scott! What's up, buddy?</THESEXA>
                    </exp-body>
                  </Exponent>
                </Section>
                """),
        ]);

        DictionaryContainerBlock exponentBlock = AllBlocks(document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 2
                && ConcatenateBlockText(block.Blocks[0]).Contains("hey especially AmE informal used as a friendly greeting", StringComparison.Ordinal))
            .Subject;

        exponentBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        exponentBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(exponentBlock.Blocks[0]).Should().Be("hey especially AmE informal used as a friendly greeting");
        ConcatenateBlockText(exponentBlock.Blocks[1]).Should().Contain("Hey, Scott! What's up, buddy?");
    }

    [Fact]
    public void RenderThesaurus_maps_exponent_labels_to_green_label_style()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderThesaurus(
        [
            Encoding.UTF8.GetBytes(
                """
                <Section>
                  <Exponent>
                    <exp-head><EXP>hey</EXP></exp-head>
                    <exp-body>
                      <LABEL>especially AmE</LABEL>
                      <LABEL> informal</LABEL>
                    </exp-body>
                  </Exponent>
                </Section>
                """),
        ]);

        document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Where(text => text.Style == DictionaryTextStyle.Label)
            .Select(text => text.Text.TrimStart())
            .Should()
            .Equal("especially AmE", "informal");
    }

    [Fact]
    public void RenderThesaurus_splits_propexa_propforms_and_examples_into_separate_blocks()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderThesaurus(
        [
            Encoding.UTF8.GetBytes(
                """
                <Section>
                  <Exponent fold="yes">
                    <exp-head>
                      <EXP>churn out/turn out</EXP>
                    </exp-head>
                    <exp-body hide="yes">
                      <DEF>to make large quantities of things, especially without caring about quality</DEF>
                      <Propexa>
                        <THESPROPFORM class="newline">churn/turn out something</THESPROPFORM>
                        <THESEXA><span class="exabullet">‧ </span>They turn out cheap souvenirs for tourists.</THESEXA>
                        <THESEXA><span class="exabullet">‧ </span>Churning out pamphlets and booklets is ineffective.</THESEXA>
                      </Propexa>
                      <Propexa>
                        <THESPROPFORM class="newline">churn/turn something out</THESPROPFORM>
                        <THESEXA><span class="exabullet">‧ </span>The company will keep turning them out.</THESEXA>
                      </Propexa>
                    </exp-body>
                  </Exponent>
                </Section>
                """),
        ]);

        DictionaryContainerBlock exponentBlock = AllBlocks(document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 3
                && ConcatenateBlockText(block.Blocks[0]).Contains("churn out/turn out to make large quantities", StringComparison.Ordinal))
            .Subject;

        exponentBlock.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        ConcatenateBlockText(exponentBlock.Blocks[0]).Should().Contain("to make large quantities");

        DictionaryContainerBlock firstPropexa = exponentBlock.Blocks[1].Should().BeOfType<DictionaryContainerBlock>().Subject;
        firstPropexa.Blocks.Should().HaveCount(3);
        firstPropexa.Blocks[0].Style.Should().Be(DictionaryBlockStyle.Normal);
        firstPropexa.Blocks.Skip(1).Should().AllSatisfy(block => block.Style.Should().Be(DictionaryBlockStyle.Example));

        document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "churn/turn out something" && text.Style == DictionaryTextStyle.Strong)
            .And
            .ContainSingle(text => text.Text == "churn/turn something out" && text.Style == DictionaryTextStyle.Strong);
    }

    [Fact]
    public void LoadContent_maps_grammar_heading_to_box_heading_block()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>make</BASE></HWD></Head>
              <Sense>
                <GramBox>
                  <HEADING>Grammar</HEADING>
                  <EXPL>Use <EXPR>made from</EXPR> especially with changed materials.</EXPL>
                </GramBox>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock grammarBox = AllBlocks(page.Document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Any(child => child.Style == DictionaryBlockStyle.BoxHeading))
            .Subject;

        grammarBox.Style.Should().Be(DictionaryBlockStyle.Box);
        grammarBox.Blocks[0].Style.Should().Be(DictionaryBlockStyle.BoxHeading);
        FlattenBlock(grammarBox.Blocks[0]).Should().Be("GRAMMAR");
        NormalizeSpaces(FlattenBlock(grammarBox.Blocks[1])).Should().Contain("Use made from");
        grammarBox.Blocks[1].Should().BeOfType<DictionaryParagraphBlock>()
            .Which.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "made from" && text.Style == DictionaryTextStyle.Emphasis);
    }

    [Fact]
    public void LoadContent_groups_asset_box_links_by_title()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test image</BASE></HWD><POS>noun</POS></Head>
              <SE_EntryAssets>
                <EntryAsset type="etymology"><Refs><Ref topic="origin"/></Refs></EntryAsset>
                <EntryAsset type="entry_collocations"><Refs><Ref topic="this-entry"/></Refs></EntryAsset>
                <EntryAsset type="other_entries_collocations"><Refs><Ref topic="other-entries"/></Refs></EntryAsset>
                <EntryAsset type="corpus_collocations"><Refs><Ref topic="corpus"/></Refs></EntryAsset>
                <EntryAsset type="thesaurus"><Refs><Ref topic="thes"/></Refs></EntryAsset>
                <EntryAsset type="word_sets"><Refs><Ref topic="sets"/></Refs></EntryAsset>
                <EntryAsset type="entry_phrases"><Refs><Ref topic="phr-this"/></Refs></EntryAsset>
                <EntryAsset type="other_entries_phrases"><Refs><Ref topic="phr-other"/></Refs></EntryAsset>
                <EntryAsset type="other_dictionary_examples"><Refs><Ref topic="dict-exa"/></Refs></EntryAsset>
                <EntryAsset type="corpus_examples"><Refs><Ref topic="corpus-exa"/></Refs></EntryAsset>
              </SE_EntryAssets>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock assetBox = page.Document.Blocks
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.AssetBox)
            .Subject;

        assetBox.Blocks.Should().HaveCount(6);
        DictionaryContainerBlock collocations = assetBox.Blocks[1].Should().BeOfType<DictionaryContainerBlock>().Subject;
        collocations.Blocks.Select(ConcatenateBlockText)
            .Should()
            .Equal("Collocations", "This Entry", "Other Entries", "Corpus");
        DictionaryParagraphBlock collocationsTitle = collocations.Blocks[0].Should().BeOfType<DictionaryParagraphBlock>().Subject;
        collocationsTitle.Inlines.OfType<DictionaryTextInline>().Should()
            .ContainSingle(text => text.Text == "Collocations" && text.Style == DictionaryTextStyle.AssetTitle);
        DictionaryContainerBlock thesaurus = assetBox.Blocks[2].Should().BeOfType<DictionaryContainerBlock>().Subject;
        thesaurus.Blocks.Select(ConcatenateBlockText)
            .Should()
            .Equal("Thesaurus", "Thesaurus", "Word Set");
        DictionaryContainerBlock web = assetBox.Blocks[5].Should().BeOfType<DictionaryContainerBlock>().Subject;
        web.Blocks.Select(ConcatenateBlockText)
            .Should()
            .Equal("Web", "Wikipedia", "Google Images");
        web.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Select(link => (link.Target.Kind, link.Target.Value))
            .Should()
            .Equal(
                (DictionaryLinkTargetKind.External, "https://en.wikipedia.org/w/index.php?search=test%20image"),
                (DictionaryLinkTargetKind.External, "https://www.google.com/images?hl=en&q=test%20image"));
    }

    [Fact]
    public void LoadContent_omits_external_asset_links_for_non_noun_entries()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test</BASE></HWD><POS>verb</POS></Head>
              <SE_EntryAssets>
                <EntryAsset type="etymology"><Refs><Ref topic="origin"/></Refs></EntryAsset>
              </SE_EntryAssets>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        DictionaryContainerBlock assetBox = page.Document.Blocks
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.AssetBox)
            .Subject;
        assetBox.Blocks.Select(ConcatenateBlockText).Should().NotContain("WebWikipediaGoogle Images");
        assetBox.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Should()
            .NotContain(link => link.Target.Kind == DictionaryLinkTargetKind.External);
    }

    [Fact]
    public void LoadContent_includes_external_asset_links_for_non_noun_entries_when_configured()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test</BASE></HWD><POS>verb</POS></Head>
              <SE_EntryAssets>
                <EntryAsset type="etymology"><Refs><Ref topic="origin"/></Refs></EntryAsset>
              </SE_EntryAssets>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(
            fixture.IndexPaths,
            fixture.DataDirectory,
            webSearchAssetBoxModeProvider: () => WebSearchAssetBoxMode.AllEntries);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        DictionaryContainerBlock assetBox = page.Document.Blocks
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.AssetBox)
            .Subject;
        assetBox.Blocks.Select(ConcatenateBlockText)
            .Should()
            .Contain("WebWikipediaGoogle Images");
        assetBox.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Should()
            .Contain(link => link.Target.Kind == DictionaryLinkTargetKind.External);
    }

    [Fact]
    public void LoadContent_uses_configured_enabled_web_search_sites_for_asset_box()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>test image</BASE></HWD><POS>noun</POS></Head>
            </Entry>
            """);
        fixture.WriteFilemap();
        WebSearchSite[] sites =
        [
            new("Images", "https://images.example/search?source=dict&q={query}"),
            new("Disabled", "https://disabled.example/?q={query}", isEnabled: false),
            new("Wiki", "https://wiki.example/?search={query}"),
        ];
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory, () => sites);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock assetBox = page.Document.Blocks
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.AssetBox)
            .Subject;
        DictionaryContainerBlock web = assetBox.Blocks
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<DictionaryContainerBlock>()
            .Subject;

        web.Blocks.Select(ConcatenateBlockText)
            .Should()
            .Equal("Web", "Images", "Wiki");
        web.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Select(link => link.Target.Value)
            .Should()
            .Equal(
                "https://images.example/search?source=dict&q=test%20image",
                "https://wiki.example/?search=test%20image");
    }

    [Fact]
    public void LoadContent_maps_span_heading_to_box_heading_block()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>make</BASE></HWD></Head>
              <Sense>
                <Hint>
                  <span class="heading">Register</span>
                  <span>In written English, prefer a more formal expression.</span>
                </Hint>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");
        DictionaryContainerBlock registerBox = AllBlocks(page.Document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Any(child => child.Style == DictionaryBlockStyle.BoxHeading))
            .Subject;

        registerBox.Style.Should().Be(DictionaryBlockStyle.Box);
        registerBox.Blocks[0].Style.Should().Be(DictionaryBlockStyle.BoxHeading);
        FlattenBlock(registerBox.Blocks[0]).Should().Be("REGISTER");
        FlattenBlock(registerBox.Blocks[1]).Should().Contain("In written English");
    }

    [Theory]
    [InlineData("<HINTBOLD>cause somebody to do something</HINTBOLD>", "cause somebody to do something")]
    [InlineData("<HINTBO>make somebody do something</HINTBO>", "make somebody do something")]
    public void LoadContent_maps_hint_bold_tags_to_strong_text(string boldXml, string expectedText)
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            $"""
            <Entry>
              <Head><HWD><BASE>make</BASE></HWD></Head>
              <Sense>
                <Hint>
                  <span class="heading">Register</span>
                  <span>Use {boldXml} in this note.</span>
                </Hint>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == expectedText && text.Style == DictionaryTextStyle.Emphasis);
    }

    [Fact]
    public void LoadContent_maps_register_expr_markup_to_strong_text()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>make</BASE></HWD></Head>
              <Sense>
                <Hint>
                  <span class="heading">Register</span>
                  <EXPL>In written English, people often use <EXPR>cause somebody to do something</EXPR> rather than <EXPR>make somebody do something</EXPR>.</EXPL>
                </Hint>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Where(text => text.Style == DictionaryTextStyle.Emphasis)
            .Select(text => text.Text)
            .Should()
            .Contain(["cause somebody to do something", "make somebody do something"]);
    }

    [Fact]
    public void LoadContent_maps_thesaurus_exponent_to_plain_emphasis()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "fs",
            "entry",
            """
            <Entry>
              <Head><HWD><BASE>make</BASE></HWD></Head>
              <Sense>
                <ThesBox>
                  <Exponent><EXP>produce</EXP><DEF>to make something</DEF></Exponent>
                </ThesBox>
              </Sense>
            </Entry>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/fs/entry");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == "produce" && text.Style == DictionaryTextStyle.Emphasis);
    }

    [Fact]
    public void RenderActivator_splits_exas_examples_and_propforms_into_separate_blocks()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderActivator(
            Encoding.UTF8.GetBytes(
                """
                <Concept id="test">
                  <HWD>TEST</HWD>
                  <Section id="s1">a test of your knowledge or skill</Section>
                </Concept>
                """),
            Encoding.UTF8.GetBytes(
                """
                <Section id="s1">
                  <SECDEF>1 a test of your knowledge or skill</SECDEF>
                  <Exponent id="p030-000197083">
                    <EXP>test</EXP>
                    <PRON><span class="neutral"> /</span>test<span class="neutral">/</span></PRON>
                    <GRAM><span class="neutral"> [</span>countable noun<span class="neutral">]</span></GRAM>
                    <DEF>a set of spoken or written questions or practical activities, which are intended to find out how much someone knows about a subject or skill</DEF>
                    <Exas>
                      <span class="neutral">: </span>
                      <EXAMPLE><span class="exabullet">▪ </span>Several students were caught cheating on the test.</EXAMPLE>
                      <EXAMPLE><span class="exabullet">▪ </span>The committee is calling for national tests for American schoolchildren.</EXAMPLE>
                      <PROPFORM>spelling/reading/biology etc test</PROPFORM>
                      <EXAMPLE><span class="exabullet">▪ </span>I have a chemistry test tomorrow.</EXAMPLE>
                      <PROPFORM>driving/driver's test</PROPFORM>
                      <EXAMPLE><span class="exabullet">▪ </span>Did Lauren pass her driving test?</EXAMPLE>
                      <PROPFORM>test on</PROPFORM>
                      <EXAMPLE><span class="exabullet">▪ </span>Listen carefully, because there will be a test on this next week.</EXAMPLE>
                    </Exas>
                  </Exponent>
                </Section>
                """),
            "s1");

        DictionaryContainerBlock exponentBlock = AllBlocks(document)
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Blocks.Count == 9
                && ConcatenateBlockText(block.Blocks[0]).Contains("test /test/ [countable noun]", StringComparison.Ordinal))
            .Subject;

        ConcatenateBlockText(exponentBlock.Blocks[0]).Should().Contain("subject or skill:");
        exponentBlock.Blocks[1].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(exponentBlock.Blocks[1]).Should().Be("\u2022 Several students were caught cheating on the test.");
        ConcatenateBlockText(exponentBlock.Blocks[3]).Should().Be("spelling/reading/biology etc test");
        exponentBlock.Blocks[4].Style.Should().Be(DictionaryBlockStyle.Example);
        ConcatenateBlockText(exponentBlock.Blocks[7]).Should().Be("test on");
        exponentBlock.Blocks[8].Style.Should().Be(DictionaryBlockStyle.Example);
    }

    [Fact]
    public void RenderActivator_maps_repeat_words_to_emphasis()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderActivator(
            Encoding.UTF8.GetBytes(
                """
                <Concept id="test">
                  <HWD>TEST</HWD>
                  <Section id="s1">2 to do a test or exam</Section>
                </Concept>
                """),
            Encoding.UTF8.GetBytes(
                """
                <Section id="s1">
                  <SECDEF>2 to do a test or exam</SECDEF>
                  <Exponent id="p030-000197211">
                    <EXP>do</EXP>
                    <PRON><span class="neutral"> /</span>duː<span class="neutral">/</span></PRON>
                    <GRAM><span class="neutral"> [</span>transitive verb<span class="neutral">]</span></GRAM>
                    <LABEL>British</LABEL>
                    <DEF><REPEAT>do</REPEAT> is more informal that<REPEAT>take</REPEAT>, and is used especially in conversation</DEF>
                  </Exponent>
                </Section>
                """),
            "s1");

        DictionaryParagraphBlock exponent = AllBlocks(document)
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => ConcatenateBlockText(block).Contains("do /duː/ [transitive verb]", StringComparison.Ordinal))
            .Subject;

        DictionaryTextInline[] emphasizedWords = exponent.Inlines
            .OfType<DictionaryTextInline>()
            .Where(text => text.Style == DictionaryTextStyle.Emphasis)
            .ToArray();
        emphasizedWords.Where(text => text.Text.Trim() == "do").Should().HaveCount(2);
        emphasizedWords.Should().ContainSingle(text => text.Text.Trim() == "take");
    }

    [Fact]
    public void RenderActivator_maps_variant_words_to_emphasis()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderActivator(
            Encoding.UTF8.GetBytes(
                """
                <Concept id="test">
                  <HWD>TEST</HWD>
                  <Section id="s1">1 a test of your knowledge or skill</Section>
                </Concept>
                """),
            Encoding.UTF8.GetBytes(
                """
                <Section id="s1">
                  <SECDEF>1 a test of your knowledge or skill</SECDEF>
                  <Exponent id="p030-000197138">
                    <EXP>oral exam</EXP>
                    <Variant>
                      <VARITYPE>also</VARITYPE>
                      <VAR>oral</VAR>
                      <LABEL>British</LABEL>
                    </Variant>
                    <PRON><span class="neutral"> /</span>ˈɔːrəl ɪgˌzæm, ˈɔːrəl<span class="neutral">/</span></PRON>
                    <GRAM><span class="neutral"> [</span>countable noun<span class="neutral">]</span></GRAM>
                    <DEF>an exam in which you answer questions by speaking</DEF>
                  </Exponent>
                </Section>
                """),
            "s1");

        DictionaryParagraphBlock exponent = AllBlocks(document)
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle(block => ConcatenateBlockText(block).Contains("oral exam also oral British", StringComparison.Ordinal))
            .Subject;

        exponent.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text.Trim() == "oral" && text.Style == DictionaryTextStyle.Emphasis)
            .And
            .ContainSingle(text => text.Text.Trim() == "also" && text.Style == DictionaryTextStyle.Normal)
            .And
            .ContainSingle(text => text.Text.Trim() == "British" && text.Style == DictionaryTextStyle.Label);
    }

    [Fact]
    public void RenderWordSets_keeps_part_of_speech_outside_reference_link()
    {
        (string title, DictionaryDocument document) = RichContentRenderer.RenderWordSets(
        [
            Encoding.UTF8.GetBytes(
                """
                <word-set>
                  <ws-head>
                    <name>Describing Somebody's Character</name>
                    <number>1</number>
                  </ws-head>
                  <Ref topic="idm.dict.example.entry">
                    <hwd>direct</hwd>
                    <pos>adj</pos>
                  </Ref>
                </word-set>
                """),
        ]);

        title.Should().Be("Word Set");
        DictionaryParagraphBlock reference = document.Blocks
            .OfType<DictionaryParagraphBlock>()
            .Should()
            .ContainSingle()
            .Subject;
        DictionaryLinkInline link = reference.Inlines
            .OfType<DictionaryLinkInline>()
            .Should()
            .ContainSingle()
            .Subject;

        ConcatenateInlineText(link.Inlines).Should().Be("direct");
        reference.Inlines.OfType<DictionaryTextInline>()
            .Should()
            .ContainSingle(text => text.Text == " adj" && text.Style == DictionaryTextStyle.PartOfSpeech);
    }

    [Fact]
    public void RenderWordFamilies_styles_part_of_speech_headings_as_word_family_text()
    {
        (string title, DictionaryDocument document) = RichContentRenderer.RenderWordFamilies(
            Encoding.UTF8.GetBytes(
                """
                <word_families>
                  <group>
                    <pos>adjective</pos>
                    <w id="happy"><span>happy</span></w>
                  </group>
                </word_families>
                """));

        title.Should().Be("Word Family");
        DictionaryHeadingBlock heading = document.Blocks
            .OfType<DictionaryHeadingBlock>()
            .Should()
            .ContainSingle()
            .Subject;
        heading.Inlines.OfType<DictionaryTextInline>().Should()
            .ContainSingle(text => text.Text == "adjective" && text.Style == DictionaryTextStyle.WordFamilyPartOfSpeech);
    }

    [Fact]
    public void RenderActivator_marks_concept_blocks_for_split_layout()
    {
        (string _, DictionaryDocument document) = RichContentRenderer.RenderActivator(
            Encoding.UTF8.GetBytes(
                """
                <Concept id="interesting">
                  <HWD>interesting</HWD>
                  <Section id="s1">jobs/books/films/activities etc</Section>
                  <References>
                    <Reference>
                      <REFTYPE>opposite</REFTYPE>
                      <Crossref><Ref topic="boring" selection="jobs">BORING / BORED</Ref></Crossref>
                    </Reference>
                  </References>
                </Concept>
                """),
            Encoding.UTF8.GetBytes(
                """
                <Section id="s1">
                  <SECDEF>jobs/books/films/activities etc</SECDEF>
                  <Exponent><EXP>interesting</EXP></Exponent>
                </Section>
                """),
            "s1");

        DictionaryContainerBlock concept = document.Blocks
            .OfType<DictionaryContainerBlock>()
            .Should()
            .ContainSingle(block => block.Style == DictionaryBlockStyle.ActivatorConcept)
            .Subject;
        string.Concat(concept.Blocks.Select(ConcatenateBlockText)).Should().Contain("Related Words");
    }

    [Fact]
    public void LoadResource_loads_picture_bytes()
    {
        using TestDictionaryFixture fixture = new();
        byte[] expected = Encoding.ASCII.GetBytes("fake-jpeg");
        fixture.AddArchiveItem("picture", "thumbnail/test.jpg", expected);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        byte[] actual = service.LoadResource(new DictionaryResourceRef("picture", "thumbnail/test.jpg", "image/jpeg"));

        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData("/activator/concept/section#exponent")]
    [InlineData("/fs/entry#sense")]
    public void LoadPlainText_ignores_entry_anchor_when_resolving_filemap_items(string path)
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem("fs", "entry", "<Entry><Head><HWD><BASE>entry</BASE></HWD></Head><Sense><DEF>Entry text.</DEF></Sense></Entry>");
        fixture.AddArchiveItem("activator_concept", "concept", "<Concept><HWD>concept</HWD></Concept>");
        fixture.AddArchiveItem("activator_section", "section", "<Section><Exponent><EXP>section</EXP></Exponent></Section>");
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        string text = service.LoadPlainText(path);

        text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LoadContent_maps_activator_cross_reference_to_activator_path()
    {
        using TestDictionaryFixture fixture = new();
        fixture.AddArchiveItem(
            "activator_concept",
            "interesting",
            """
            <Concept id="interesting">
              <HWD>interesting</HWD>
              <Section id="s1"><SECNR>1</SECNR>jobs/books/films/activities etc</Section>
              <References>
                <Reference>
                  <REFTYPE>opposite</REFTYPE>
                  <Crossref>
                    <Ref topic="boring" selection="jobs">BORING / BORED</Ref>
                  </Crossref>
                </Reference>
              </References>
            </Concept>
            """);
        fixture.AddArchiveItem(
            "activator_section",
            "s1",
            """
            <Section id="s1">
              <SECDEF><SECNR>1</SECNR>jobs/books/films/activities etc</SECDEF>
              <Exponent><EXP>interesting</EXP></Exponent>
            </Section>
            """);
        fixture.WriteFilemap();
        using DictionaryContentService service = new(fixture.IndexPaths, fixture.DataDirectory);

        DictionaryContentPage page = service.LoadContent("/activator/interesting/s1");

        page.Document.Blocks.SelectMany(FlattenInlines)
            .OfType<DictionaryLinkInline>()
            .Should()
            .ContainSingle(link =>
                link.Target.Kind == DictionaryLinkTargetKind.DictionaryPath
                && link.Target.Value == "/activator/boring/jobs");
    }

    private sealed class TestDictionaryFixture : IDisposable
    {
        private readonly List<(string Archive, string Name, ArchiveLocation Location)> _filemapEntries = [];
        private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public TestDictionaryFixture()
        {
            IndexDirectory = Path.Combine(_root, "index");
            DataDirectory = Path.Combine(_root, "ldoce5.data");
            Directory.CreateDirectory(IndexDirectory);
            Directory.CreateDirectory(DataDirectory);
            IndexPaths = new IndexPaths(IndexDirectory);
        }

        public string IndexDirectory { get; }

        public IndexPaths IndexPaths { get; }

        public string DataDirectory { get; }

        public void AddArchiveItem(string archive, string name, string xml)
        {
            AddArchiveItem(archive, name, Encoding.UTF8.GetBytes(xml));
        }

        public void AddArchiveItem(string archive, string name, byte[] raw)
        {
            string contentPath = Path.Combine(DataDirectory, GetArchiveDirectory(archive), "files.skn", "CONTENT.tda");
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);

            byte[] compressed = Compress(raw);
            long compressedOffset = 0;
            if (File.Exists(contentPath))
            {
                compressedOffset = new FileInfo(contentPath).Length;
            }

            using (FileStream stream = File.Open(contentPath, FileMode.Append, FileAccess.Write))
            {
                stream.Write(compressed);
            }

            ArchiveLocation location = new(
                checked((int)compressedOffset),
                compressed.Length,
                0,
                raw.Length);
            _filemapEntries.Add((archive, name, location));
        }

        public void WriteFilemap()
        {
            using FileStream stream = File.Create(IndexPaths.FilemapPath);
            ConstantDatabaseWriter writer = new(stream);
            foreach ((string archive, string name, ArchiveLocation location) in _filemapEntries)
            {
                writer.Add(CreateFilemapKey(archive, name), PackLocation(location));
            }

            writer.FinalizeDatabase();
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static string GetArchiveDirectory(string archive)
        {
            return archive switch
            {
                "activator_concept" => Path.Combine("activator.skn", "activator_concept.skn"),
                "activator_section" => Path.Combine("activator.skn", "activator_section.skn"),
                "fs" => "fs.skn",
                "picture" => "picture.skn",
                _ => throw new ArgumentOutOfRangeException(nameof(archive), archive, "Unsupported test archive."),
            };
        }

        private static byte[] Compress(byte[] raw)
        {
            using MemoryStream destination = new();
            using (System.IO.Compression.ZLibStream zlib = new(destination, System.IO.Compression.CompressionLevel.SmallestSize))
            {
                zlib.Write(raw);
            }

            return destination.ToArray();
        }

        private static byte[] CreateFilemapKey(string archive, string name)
        {
            byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes($"{archive}:{name}"));
            byte[] key = new byte[10];
            Buffer.BlockCopy(hash, 0, key, 0, key.Length);
            return key;
        }

        private static byte[] PackLocation(ArchiveLocation location)
        {
            byte[] data = new byte[10];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), checked((uint)location.CompressedOffset));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), checked((ushort)location.CompressedSize));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), checked((ushort)location.OriginalOffset));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), checked((ushort)location.OriginalSize));
            return data;
        }
    }

    private static string Flatten(DictionaryDocument document)
    {
        return NormalizeSpaces(string.Join(" ", document.Blocks.Select(FlattenBlock)));
    }

    private static IEnumerable<DictionaryBlock> AllBlocks(DictionaryDocument document)
    {
        return document.Blocks.SelectMany(AllBlocks);
    }

    private static IEnumerable<DictionaryBlock> AllBlocks(DictionaryBlock block)
    {
        yield return block;
        if (block is DictionaryContainerBlock container)
        {
            foreach (DictionaryBlock child in container.Blocks.SelectMany(AllBlocks))
            {
                yield return child;
            }
        }
    }

    private static string FlattenBlock(DictionaryBlock block)
    {
        return block switch
        {
            DictionaryParagraphBlock paragraph => FlattenInlineText(paragraph.Inlines),
            DictionaryHeadingBlock heading => FlattenInlineText(heading.Inlines),
            DictionaryContainerBlock container => string.Join(" ", container.Blocks.Select(FlattenBlock)),
            _ => string.Empty,
        };
    }

    private static IEnumerable<DictionaryInline> FlattenInlines(DictionaryBlock block)
    {
        return block switch
        {
            DictionaryParagraphBlock paragraph => FlattenInlines(paragraph.Inlines),
            DictionaryHeadingBlock heading => FlattenInlines(heading.Inlines),
            DictionaryContainerBlock container => container.Blocks.SelectMany(FlattenInlines),
            _ => [],
        };
    }

    private static IEnumerable<DictionaryInline> FlattenInlines(IEnumerable<DictionaryInline> inlines)
    {
        foreach (DictionaryInline inline in inlines)
        {
            yield return inline;
            if (inline is DictionaryLinkInline link)
            {
                foreach (DictionaryInline child in FlattenInlines(link.Inlines))
                {
                    yield return child;
                }
            }
        }
    }

    private static string FlattenInlineText(IEnumerable<DictionaryInline> inlines)
    {
        return string.Join(
            " ",
            FlattenInlines(inlines)
                .OfType<DictionaryTextInline>()
                .Select(inline => inline.Text));
    }

    private static string ConcatenateInlineText(IEnumerable<DictionaryInline> inlines)
    {
        return string.Concat(
            FlattenInlines(inlines)
                .OfType<DictionaryTextInline>()
                .Select(inline => inline.Text));
    }

    private static string ConcatenateBlockText(DictionaryBlock block)
    {
        return block switch
        {
            DictionaryParagraphBlock paragraph => ConcatenateInlineText(paragraph.Inlines),
            DictionaryHeadingBlock heading => ConcatenateInlineText(heading.Inlines),
            DictionaryContainerBlock container => string.Concat(container.Blocks.Select(ConcatenateBlockText)),
            _ => string.Empty,
        };
    }

    private static string NormalizeSpaces(string text)
    {
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
