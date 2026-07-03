using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Converts LDOCE XML documents into native rich document models.
/// </summary>
public static class RichContentRenderer
{
    private static readonly HashSet<string> SkippedTags = ["ACTIV", "INFLX", "OBJECT", "SE_EntryAssets", "EntryAsset"];

    private static readonly HashSet<string> BlockTags =
    [
        "ColloBox",
        "Collocate",
        "ColloExa",
        "COLLEXA",
        "Crossref",
        "Deriv",
        "Entry",
        "EXAMPLE",
        "Exponent",
        "F2NBox",
        "GramBox",
        "GramExa",
        "HEAD",
        "HEADING",
        "Head",
        "Hint",
        "ILLUSTRATION",
        "PhrVbEntry",
        "Propexa",
        "RunOn",
        "SECHEADING",
        "SENSE",
        "Section",
        "Sense",
        "SpokenSect",
        "Subsense",
        "Tail",
        "ThesBox",
        "THESEXA",
    ];

    /// <summary>
    /// Renders a dictionary entry page.
    /// </summary>
    /// <param name="data">Raw entry XML.</param>
    /// <param name="webSearchSites">Configured web search engines.</param>
    /// <param name="webSearchAssetBoxMode">Configured AssetBox web search display mode.</param>
    /// <returns>Title and rich document.</returns>
    public static (string Title, DictionaryDocument Document) RenderEntry(
        byte[] data,
        IEnumerable<WebSearchSite>? webSearchSites = null,
        WebSearchAssetBoxMode webSearchAssetBoxMode = WebSearchAssetBoxMode.NounEntriesOnly)
    {
        XElement root = ParseRoot(data);
        string title = GetEntryTitle(root);
        List<DictionaryBlock> blocks = ParseFlow(root);
        DictionaryContainerBlock? assets = RenderEntryAssets(root, webSearchSites, webSearchAssetBoxMode);
        if (assets is not null)
        {
            int insertIndex = blocks.Count > 0 && blocks[0].Style == DictionaryBlockStyle.EntryHead ? 1 : 0;
            blocks.Insert(insertIndex, assets);
        }

        return (title, new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders a collocations page.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderCollocations(byte[] data)
    {
        XElement root = ParseRoot(data);
        return ("Collocations", new DictionaryDocument(ParseFlow(root)));
    }

    /// <summary>
    /// Renders an examples page.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderExamples(byte[] data)
    {
        XElement root = ParseRoot(data);
        List<DictionaryBlock> blocks = [];
        string headword = GetText(root.Element("exa-head")?.Element("hwd"));
        string pos = GetText(root.Element("exa-head")?.Element("pos"));
        if (!string.IsNullOrWhiteSpace(headword))
        {
            blocks.Add(new DictionaryHeadingBlock(
                1,
                [
                    new DictionaryTextInline(headword, DictionaryTextStyle.Headword),
                    new DictionaryTextInline(pos.Length > 0 ? $" {pos}" : string.Empty, DictionaryTextStyle.Label),
                ],
                null));
        }

        foreach (XElement example in root.Descendants("exa"))
        {
            blocks.Add(new DictionaryParagraphBlock(
                InlineChildren(example),
                DictionaryBlockStyle.Example,
                GetAnchor(example)));
        }

        return (headword.Length > 0 ? headword : "Examples", new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders word-family entries.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderWordFamilies(byte[] data)
    {
        XElement root = ParseRoot(data);
        List<DictionaryBlock> blocks = [];
        foreach (XElement group in root.Elements("group"))
        {
            string pos = GetText(group.Element("pos"));
            if (pos.Length > 0)
            {
                blocks.Add(new DictionaryHeadingBlock(2, [new DictionaryTextInline(pos, DictionaryTextStyle.WordFamilyPartOfSpeech)], null));
            }

            foreach (XElement word in group.Elements("w"))
            {
                blocks.Add(new DictionaryParagraphBlock(InlineChildren(word), DictionaryBlockStyle.Normal, GetAnchor(word)));
            }
        }

        return ("Word Family", new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders an etymology page.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderEtymologies(byte[] data)
    {
        XElement root = ParseRoot(data);
        return ("Origin", new DictionaryDocument(ParseFlow(root)));
    }

    /// <summary>
    /// Renders phrase bank entries.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderPhrases(byte[] data)
    {
        XElement root = ParseRoot(data);
        List<DictionaryBlock> blocks = [];
        foreach (XElement phrase in root.Elements("phrase"))
        {
            XElement? reference = phrase.Element("phrase-head")?.Element("Ref");
            if (reference is not null)
            {
                blocks.Add(new DictionaryHeadingBlock(2, InlineFromReference(reference, inheritedAnchor: null), null));
            }

            foreach (XElement example in phrase.Descendants("exa"))
            {
                blocks.Add(new DictionaryParagraphBlock(
                    InlineChildren(example),
                    DictionaryBlockStyle.Example,
                    GetAnchor(example)));
            }
        }

        return ("Phrase Bank", new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders thesaurus pages.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderThesaurus(IEnumerable<byte[]> dataSet)
    {
        List<DictionaryBlock> blocks = [];
        foreach (byte[] data in dataSet)
        {
            XElement root = ParseRoot(data);
            XElement? heading = root.Element("SECHEADING");
            if (heading is not null)
            {
                blocks.Add(new DictionaryHeadingBlock(2, InlineChildren(heading), GetAnchor(heading)));
            }

            foreach (XElement exponent in root.Elements("Exponent"))
            {
                blocks.Add(ToBlock(exponent));
            }
        }

        return ("Thesaurus", new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders word set pages.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderWordSets(IEnumerable<byte[]> dataSet)
    {
        List<DictionaryBlock> blocks = [];
        foreach (byte[] data in dataSet)
        {
            XElement root = ParseRoot(data);
            string name = GetText(root.Element("ws-head")?.Element("name"));
            string number = GetText(root.Element("ws-head")?.Element("number"));
            blocks.Add(new DictionaryHeadingBlock(
                2,
                [new DictionaryTextInline(number.Length > 0 ? $"{name} ({number})" : name, DictionaryTextStyle.Headword)],
                null));
            foreach (XElement reference in root.Descendants("Ref"))
            {
                blocks.Add(new DictionaryParagraphBlock(InlineFromWordSetReference(reference), DictionaryBlockStyle.Normal, null));
            }
        }

        return ("Word Set", new DictionaryDocument(blocks));
    }

    /// <summary>
    /// Renders an activator concept and selected section.
    /// </summary>
    public static (string Title, DictionaryDocument Document) RenderActivator(byte[] conceptData, byte[] sectionData, string selectedSectionId)
    {
        XElement conceptRoot = ParseRoot(conceptData);
        XElement sectionRoot = ParseRoot(sectionData);
        List<DictionaryBlock> blocks = [];

        string sectionTitle = GetText(sectionRoot.Element("SECDEF"));
        if (sectionTitle.Length > 0)
        {
            blocks.Add(new DictionaryHeadingBlock(1, [new DictionaryTextInline(sectionTitle, DictionaryTextStyle.Headword)], GetAnchor(sectionRoot)));
        }

        foreach (XElement exponent in sectionRoot.Elements("Exponent"))
        {
            blocks.Add(ToBlock(exponent));
        }

        string conceptTitle = GetText(conceptRoot.Element("HWD")).Replace("/", " / ", StringComparison.Ordinal);
        List<DictionaryBlock> conceptBlocks = [];
        conceptBlocks.Add(new DictionaryHeadingBlock(1, [new DictionaryTextInline(conceptTitle, DictionaryTextStyle.Headword)], null));
        foreach (XElement element in conceptRoot.Elements())
        {
            if (element.Name.LocalName == "SUBHWD")
            {
                conceptBlocks.Add(new DictionaryHeadingBlock(2, [new DictionaryTextInline(GetText(element), DictionaryTextStyle.Label)], null));
            }
            else if (element.Name.LocalName == "Section")
            {
                string sectionId = (string?)element.Attribute("id") ?? string.Empty;
                string conceptId = (string?)conceptRoot.Attribute("id") ?? string.Empty;
                DictionaryLinkInline link = new(
                    new DictionaryLinkTarget(DictionaryLinkTargetKind.DictionaryPath, $"/activator/{conceptId}/{sectionId}"),
                    [new DictionaryTextInline(GetText(element), DictionaryTextStyle.Normal)]);
                DictionaryBlockStyle style = sectionId == selectedSectionId
                    ? DictionaryBlockStyle.ActivatorSection
                    : DictionaryBlockStyle.Normal;
                conceptBlocks.Add(new DictionaryParagraphBlock([link], style, sectionId));
            }
        }

        XElement[] references = conceptRoot.Descendants("References").Descendants("Reference").ToArray();
        if (references.Length > 0)
        {
            conceptBlocks.Add(new DictionaryHeadingBlock(2, [new DictionaryTextInline("Related Words", DictionaryTextStyle.Label)], null));
        }

        foreach (XElement reference in references)
        {
            conceptBlocks.Add(ToBlock(reference));
        }

        blocks.Add(new DictionaryContainerBlock(conceptBlocks, DictionaryBlockStyle.ActivatorConcept, null));
        return ($"{conceptTitle} {sectionTitle}".Trim(), new DictionaryDocument(blocks));
    }

    private static XElement ParseRoot(byte[] data)
    {
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(data), LoadOptions.None);
        return document.Root ?? throw new InvalidDataException("Dictionary XML has no root element.");
    }

    private static string GetEntryTitle(XElement root)
    {
        XElement? head = root.Element("Head");
        string title = GetText(head?.Element("HWD")?.Element("BASE"));
        string[] partsOfSpeech = head?.Elements("POS").Select(GetText).Where(text => text.Length > 0).ToArray() ?? [];
        return partsOfSpeech.Length == 0 ? title : $"{title} ({string.Join(", ", partsOfSpeech)})";
    }

    private static List<DictionaryBlock> ParseFlow(XElement element)
    {
        List<DictionaryBlock> blocks = [];
        List<DictionaryInline> current = [];
        List<string> currentAnchorAliases = [];

        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(current, text.Value, DictionaryTextStyle.Normal);
                continue;
            }

            if (node is not XElement child || SkippedTags.Contains(child.Name.LocalName))
            {
                continue;
            }

            if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, currentAnchorAliases);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, GetAnchor(element), currentAnchorAliases);
        return blocks;
    }

    private static DictionaryBlock ToBlock(XElement element)
    {
        string tag = element.Name.LocalName;
        string? anchor = GetAnchor(element);
        return tag switch
        {
            "HEAD" => AddAnchorAliases(new DictionaryParagraphBlock(InlineChildren(element), DictionaryBlockStyle.EntryHead, anchor), element),
            "Head" => AddAnchorAliases(new DictionaryParagraphBlock(InlineChildren(element), DictionaryBlockStyle.EntryHead, anchor), element),
            "HEADING" => AddAnchorAliases(ToBoxHeadingBlock(element, anchor), element),
            "span" when IsHeadingSpan(element) => AddAnchorAliases(ToBoxHeadingBlock(element, anchor), element),
            "ILLUSTRATION" => ToIllustrationBlock(element),
            "SECHEADING" => AddAnchorAliases(new DictionaryHeadingBlock(2, InlineChildren(element), anchor), element),
            "EXAMPLE" or "COLLEXA" or "THESEXA" => ToExampleBlock(element, anchor),
            _ => ToContainerBlock(element, tag, anchor),
        };
    }

    private static DictionaryParagraphBlock ToExampleBlock(XElement element, string? anchor)
    {
        IReadOnlyList<DictionaryInline> inlines = InlineChildren(element);
        if (element.Name.LocalName == "COLLEXA" && !StartsWithExampleBullet(inlines))
        {
            List<DictionaryInline> prefixed = [new DictionaryTextInline("\u2022 ", DictionaryTextStyle.Example)];
            prefixed.AddRange(inlines);
            inlines = prefixed;
        }

        return AddAnchorAliases(new DictionaryParagraphBlock(inlines, DictionaryBlockStyle.Example, anchor), element);
    }

    private static bool StartsWithExampleBullet(IEnumerable<DictionaryInline> inlines)
    {
        string text = GetFirstVisibleText(inlines).TrimStart();
        return text.StartsWith('\u2022');
    }

    private static string GetFirstVisibleText(IEnumerable<DictionaryInline> inlines)
    {
        foreach (DictionaryInline inline in inlines)
        {
            switch (inline)
            {
                case DictionaryTextInline textInline when textInline.Text.Length > 0:
                    return textInline.Text;
                case DictionaryLinkInline linkInline:
                    string text = GetFirstVisibleText(linkInline.Inlines);
                    if (text.Length > 0)
                    {
                        return text;
                    }

                    break;
                case DictionaryLineBreakInline:
                    return string.Empty;
            }
        }

        return string.Empty;
    }

    private static DictionaryContainerBlock ToContainerBlock(XElement element, string tag, string? anchor)
    {
        List<DictionaryBlock> blocks = tag switch
        {
            "Collocate" => ParseCollocateFlow(element),
            "Exponent" => ParseExponentFlow(element),
            _ => ParseFlow(element),
        };
        if (anchor is not null && ShouldPromoteContainerAnchor(tag) && TryAddAnchorAlias(blocks, anchor))
        {
            anchor = null;
        }

        return new DictionaryContainerBlock(blocks, GetBlockStyle(tag), anchor);
    }

    private static List<DictionaryBlock> ParseCollocateFlow(XElement element)
    {
        return element.Elements().Any(child => child.Name.LocalName is "coll-head" or "coll-body")
            ? ParseWrappedCollocateFlow(element)
            : ParseInlineHeadThenBlocksFlow(element);
    }

    private static List<DictionaryBlock> ParseWrappedCollocateFlow(XElement element)
    {
        List<DictionaryBlock> blocks = [];
        List<DictionaryInline> current = [];
        foreach (XElement child in element.Elements())
        {
            string tag = child.Name.LocalName;
            if (tag == "coll-head")
            {
                AddFlowInlines(current, child);
            }
            else if (tag == "coll-body")
            {
                AddWrappedCollocateBodyBlocks(blocks, current, child);
            }
            else if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, []);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, GetAnchor(element), []);
        return blocks;
    }

    private static void AddWrappedCollocateBodyBlocks(List<DictionaryBlock> blocks, List<DictionaryInline> current, XElement body)
    {
        foreach (XNode node in body.Nodes())
        {
            if (node is XText text)
            {
                AddText(current, text.Value, DictionaryTextStyle.Normal);
                continue;
            }

            if (node is not XElement child || SkippedTags.Contains(child.Name.LocalName))
            {
                continue;
            }

            if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, []);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }
    }

    private static List<DictionaryBlock> ParseInlineHeadThenBlocksFlow(XElement element)
    {
        List<DictionaryBlock> blocks = [];
        List<DictionaryInline> current = [];
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(current, text.Value, DictionaryTextStyle.Normal);
                continue;
            }

            if (node is not XElement child || SkippedTags.Contains(child.Name.LocalName))
            {
                continue;
            }

            if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, []);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, GetAnchor(element), []);
        return blocks;
    }

    private static List<DictionaryBlock> ParseExponentFlow(XElement element)
    {
        List<DictionaryBlock> blocks = [];
        List<DictionaryInline> current = [];
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(current, text.Value, DictionaryTextStyle.Normal);
                continue;
            }

            if (node is not XElement child || SkippedTags.Contains(child.Name.LocalName))
            {
                continue;
            }

            string tag = child.Name.LocalName;
            if (tag == "exp-head")
            {
                AddFlowInlines(current, child);
            }
            else if (tag == "exp-body")
            {
                AddExponentBodyBlocks(blocks, current, child);
            }
            else if (tag == "Exas")
            {
                AddExponentBodyBlocks(blocks, current, child);
            }
            else if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, []);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, GetAnchor(element), []);
        return blocks;
    }

    private static void AddExponentBodyBlocks(List<DictionaryBlock> blocks, List<DictionaryInline> current, XElement body)
    {
        foreach (XNode node in body.Nodes())
        {
            if (node is XText text)
            {
                AddText(current, text.Value, DictionaryTextStyle.Normal);
                continue;
            }

            if (node is not XElement child || SkippedTags.Contains(child.Name.LocalName))
            {
                continue;
            }

            if (IsBlockElement(child))
            {
                FlushParagraph(blocks, current, DictionaryBlockStyle.Normal, null, []);
                blocks.Add(ToBlock(child));
            }
            else
            {
                current.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }
    }

    private static void AddFlowInlines(List<DictionaryInline> target, XElement element)
    {
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(target, text.Value, DictionaryTextStyle.Normal);
            }
            else if (node is XElement child && !SkippedTags.Contains(child.Name.LocalName))
            {
                target.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }
    }

    private static bool ShouldPromoteContainerAnchor(string tag)
    {
        return tag is "Collocate" or "Exponent";
    }

    private static bool TryAddAnchorAlias(IReadOnlyList<DictionaryBlock> blocks, string anchor)
    {
        DictionaryBlock? block = blocks.FirstOrDefault();
        if (block is null)
        {
            return false;
        }

        if (string.Equals(block.Anchor, anchor, StringComparison.Ordinal)
            || block.AnchorAliases.Contains(anchor, StringComparer.Ordinal))
        {
            return true;
        }

        block.AnchorAliases = [.. block.AnchorAliases, anchor];
        return true;
    }

    private static DictionaryBlockStyle GetBlockStyle(string tag)
    {
        return tag switch
        {
            "Sense" or "SENSE" or "Subsense" => DictionaryBlockStyle.Sense,
            "ColloBox" or "ThesBox" or "GramBox" or "F2NBox" or "Hint" => DictionaryBlockStyle.Box,
            "Exponent" or "Collocate" => DictionaryBlockStyle.Box,
            "Section" => DictionaryBlockStyle.ActivatorSection,
            _ => DictionaryBlockStyle.Normal,
        };
    }

    private static bool IsBlockElement(XElement element)
    {
        return BlockTags.Contains(element.Name.LocalName) || IsHeadingSpan(element);
    }

    private static bool IsHeadingSpan(XElement element)
    {
        return element.Name.LocalName == "span"
            && string.Equals((string?)element.Attribute("class"), "heading", StringComparison.Ordinal);
    }

    private static DictionaryParagraphBlock ToBoxHeadingBlock(XElement element, string? anchor)
    {
        return new DictionaryParagraphBlock(UppercaseTextInlines(InlineChildren(element)), DictionaryBlockStyle.BoxHeading, anchor);
    }

    private static IReadOnlyList<DictionaryInline> UppercaseTextInlines(IReadOnlyList<DictionaryInline> inlines)
    {
        List<DictionaryInline> result = [];
        foreach (DictionaryInline inline in inlines)
        {
            result.Add(inline switch
            {
                DictionaryTextInline text => text with { Text = text.Text.ToUpperInvariant() },
                DictionaryLinkInline link => link with { Inlines = UppercaseTextInlines(link.Inlines) },
                _ => inline,
            });
        }

        return result;
    }

    private static IReadOnlyList<DictionaryInline> InlineChildren(XElement element)
    {
        List<DictionaryInline> inlines = [];
        DictionaryTextStyle style = GetTextStyle(element);
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(inlines, text.Value, style);
            }
            else if (node is XElement child && !SkippedTags.Contains(child.Name.LocalName))
            {
                inlines.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        return CompactInlines(inlines);
    }

    private static IReadOnlyList<DictionaryInline> InlineFromElement(XElement element, string? inheritedAnchor)
    {
        string tag = element.Name.LocalName;
        if (SkippedTags.Contains(tag))
        {
            return [];
        }

        if (tag == "span" && string.Equals((string?)element.Attribute("class"), "exabullet", StringComparison.Ordinal))
        {
            return [new DictionaryTextInline("\u2022 ", DictionaryTextStyle.Example, inheritedAnchor)];
        }

        return tag switch
        {
            "br" => [new DictionaryLineBreakInline()],
            "Audio" => ToAudioInline(element),
            "ILLUSTRATION" => ToIllustrationInline(element),
            "Ref" => InlineFromReference(element, inheritedAnchor),
            "NonDV" => InlineFromNonDv(element, inheritedAnchor),
            "HWD" when element.Parent?.Name.LocalName == "Head" && element.Parent.Element("HYPHENATION") is null =>
                [new DictionaryTextInline(GetText(element.Element("BASE")) + " ", DictionaryTextStyle.Headword)],
            "HWD" when element.Parent?.Name.LocalName == "Head" => [],
            _ => WrapInlineElement(element, inheritedAnchor),
        };
    }

    private static IReadOnlyList<DictionaryInline> InlineFromReference(XElement element, string? inheritedAnchor)
    {
        string href = GetReferenceHref(element);
        List<DictionaryInline> children = [];
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(children, text.Value, DictionaryTextStyle.Strong, inheritedAnchor);
                continue;
            }

            if (node is not XElement child)
            {
                continue;
            }

            if (child.Name.LocalName == "SUFFIX")
            {
                AddText(children, GetText(child), DictionaryTextStyle.Strong, inheritedAnchor);
            }
            else
            {
                children.AddRange(InlineFromElement(child, inheritedAnchor));
            }
        }

        if (children.Count == 0)
        {
            AddText(children, GetText(element), DictionaryTextStyle.Strong, inheritedAnchor);
        }

        return [new DictionaryLinkInline(new DictionaryLinkTarget(DictionaryLinkTargetKind.DictionaryPath, href), CompactInlines(children))];
    }

    private static IReadOnlyList<DictionaryInline> InlineFromWordSetReference(XElement element)
    {
        string href = GetReferenceHref(element);
        List<DictionaryInline> linkChildren = [];
        List<DictionaryInline> result = [];
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(linkChildren, text.Value, DictionaryTextStyle.Strong);
                continue;
            }

            if (node is not XElement child)
            {
                continue;
            }

            string tag = child.Name.LocalName;
            if (IsPartOfSpeechElement(child))
            {
                string pos = GetText(child);
                if (pos.Length > 0)
                {
                    result.Add(new DictionaryTextInline(" " + pos, DictionaryTextStyle.PartOfSpeech));
                }
            }
            else if (tag is "hwd" or "HWD" or "SUFFIX")
            {
                AddText(linkChildren, GetText(child), DictionaryTextStyle.Strong);
            }
            else
            {
                linkChildren.AddRange(InlineFromElement(child, inheritedAnchor: null));
            }
        }

        if (linkChildren.Count == 0)
        {
            AddText(linkChildren, GetText(element), DictionaryTextStyle.Strong);
        }

        result.Insert(0, new DictionaryLinkInline(new DictionaryLinkTarget(DictionaryLinkTargetKind.DictionaryPath, href), CompactInlines(linkChildren)));
        return result;
    }

    private static string GetReferenceHref(XElement element)
    {
        string topic = (string?)element.Attribute("topic") ?? string.Empty;
        string selection = (string?)element.Attribute("selection") ?? string.Empty;
        string bookmark = (string?)element.Attribute("bookmark") ?? string.Empty;
        if (topic.Length > 0 && selection.Length > 0)
        {
            return $"/activator/{topic}/{selection}";
        }

        if (topic.Split('.').Length == 4)
        {
            string href = "/fs/" + IdmArchive.ShortenId(topic);
            if (bookmark.Length > 0)
            {
                href += "#" + IdmArchive.ShortenId(bookmark);
            }

            return href;
        }

        return topic;
    }

    private static bool IsPartOfSpeechElement(XElement element)
    {
        return string.Equals(element.Name.LocalName, "pos", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DictionaryInline> InlineFromNonDv(XElement element, string? inheritedAnchor)
    {
        string text = GetText(element.Element("REFHWD"));
        string suffix = GetText(element.Element("SUFFIX"));
        string query = (text + suffix).Trim();
        if (query.Length == 0)
        {
            return [];
        }

        return
        [
            new DictionaryLinkInline(
                new DictionaryLinkTarget(DictionaryLinkTargetKind.Lookup, query),
                [new DictionaryTextInline(query, DictionaryTextStyle.Strong, inheritedAnchor)]),
        ];
    }

    private static IReadOnlyList<DictionaryInline> WrapInlineElement(XElement element, string? inheritedAnchor)
    {
        DictionaryTextStyle style = GetTextStyle(element);
        string? anchor = GetAnchor(element) ?? inheritedAnchor;
        List<DictionaryInline> children = [];
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(children, text.Value, style, anchor);
            }
            else if (node is XElement child)
            {
                children.AddRange(InlineFromElement(child, anchor));
            }
        }

        return CompactInlines(children);
    }

    private static IReadOnlyList<DictionaryInline> ToAudioInline(XElement element)
    {
        string topic = (string?)element.Attribute("topic") ?? string.Empty;
        string name = topic.Split('/').LastOrDefault() ?? string.Empty;
        string resource = ((string?)element.Attribute("resource") ?? string.Empty).ToLowerInvariant();
        string title = resource switch
        {
            "gb_hwd_pron" => "Play British pronunciation",
            "us_hwd_pron" => "Play American pronunciation",
            "exa_pron" or "sfx" => "Play example audio",
            _ => "Play audio",
        };
        return name.Length == 0 ? [] : [new DictionaryAudioInline(new DictionaryResourceRef(resource, name, "audio/mpeg"), title)];
    }

    private static IReadOnlyList<DictionaryInline> ToIllustrationInline(XElement element)
    {
        string topic = (string?)element.Attribute("thumb") ?? string.Empty;
        string filename = topic.Split('/').LastOrDefault() ?? string.Empty;
        if (filename.Length == 0)
        {
            return [];
        }

        DictionaryResourceRef thumbnail = new("picture", "thumbnail/" + filename, "image/jpeg");
        DictionaryLinkTarget target = new(DictionaryLinkTargetKind.Image, "/picture/fullsize/" + filename);
        return [new DictionaryImageInline(thumbnail, target)];
    }

    private static DictionaryBlock ToIllustrationBlock(XElement element)
    {
        string topic = (string?)element.Attribute("thumb") ?? string.Empty;
        string filename = topic.Split('/').LastOrDefault() ?? string.Empty;
        if (filename.Length == 0)
        {
            return AddAnchorAliases(new DictionaryParagraphBlock([], DictionaryBlockStyle.Normal, GetAnchor(element)), element);
        }

        DictionaryResourceRef thumbnail = new("picture", "thumbnail/" + filename, "image/jpeg");
        DictionaryLinkTarget target = new(DictionaryLinkTargetKind.Image, "/picture/fullsize/" + filename);
        return AddAnchorAliases(new DictionaryImageBlock(thumbnail, null, GetAnchor(element)) { Target = target }, element);
    }

    private static DictionaryTextStyle GetTextStyle(XElement element)
    {
        string tag = element.Name.LocalName;
        string cls = (string?)element.Attribute("class") ?? string.Empty;

        if (cls == "sensenum")
        {
            return DictionaryTextStyle.Badge;
        }

        if (cls == "synopp")
        {
            return DictionaryTextStyle.RelationTag;
        }

        if (cls == "lead")
        {
            return DictionaryTextStyle.Emphasis;
        }

        return tag switch
        {
            "SIGNPOST" => DictionaryTextStyle.Signpost,
            "SYNOPP" => DictionaryTextStyle.RelationTag,
            "FREQ" => DictionaryTextStyle.FrequencyTag,
            "b" when IsInsideExample(element) => DictionaryTextStyle.ExampleStrong,
            "b" => DictionaryTextStyle.Emphasis,
            "COLLOINEXA" => DictionaryTextStyle.ExampleStrong,
            "HINTBO" or "HINTBOLD" or "DEFBOLD" or "EXPR" or "EXP" or "COLLOC" or "LEXVAR" or "VAR" or "REPEAT" => DictionaryTextStyle.Emphasis,
            "PHRVBHWD" => DictionaryTextStyle.Headword,
            "HWD" when element.Parent?.Name.LocalName == "HEAD" => DictionaryTextStyle.Hyphenation,
            "BASE" when IsInsideExample(element) => DictionaryTextStyle.Example,
            "HYPHENATION" when element.Parent?.Name.LocalName == "Head" => DictionaryTextStyle.Hyphenation,
            "HOMNUM" => DictionaryTextStyle.HomonymNumber,
            "BASE" or "HYPHENATION" or "COLLO" => DictionaryTextStyle.Strong,
            "POS" or "GRAM" or "GEO" or "LABEL" or "REGISTERLAB" or "FIELD" or "SECNR" => DictionaryTextStyle.Label,
            "DEF" => DictionaryTextStyle.Definition,
            "EXAMPLE" or "exa" => DictionaryTextStyle.Example,
            "LEXUNIT" or "PROPFORM" or "PROPFORMPREP" or "REFHWD" or "THESPROPFORM" => DictionaryTextStyle.Strong,
            "ORIGIN" => DictionaryTextStyle.Origin,
            _ => DictionaryTextStyle.Normal,
        };
    }

    private static bool IsInsideExample(XElement element)
    {
        return element.Ancestors().Any(ancestor =>
            ancestor.Name.LocalName is "EXAMPLE" or "exa" or "ColloExa" or "COLLEXA" or "GramExa" or "THESEXA");
    }

    private static T AddAnchorAliases<T>(T block, XElement element)
        where T : DictionaryBlock
    {
        block.AnchorAliases = GetBlockAnchors(element)
            .Where(anchor => !string.Equals(anchor, block.Anchor, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return block;
    }

    private static DictionaryContainerBlock? RenderEntryAssets(
        XElement root,
        IEnumerable<WebSearchSite>? webSearchSites,
        WebSearchAssetBoxMode webSearchAssetBoxMode)
    {
        Dictionary<string, string> assets = [];
        foreach (XElement asset in root.Descendants("EntryAsset"))
        {
            string type = ((string?)asset.Attribute("type") ?? string.Empty).ToLowerInvariant();
            string names = string.Join("_", asset.Descendants("Ref").Select(reference => (string?)reference.Attribute("topic")).Where(topic => !string.IsNullOrWhiteSpace(topic)));
            if (type.Length > 0 && names.Length > 0)
            {
                assets[type] = names;
            }
        }

        List<DictionaryBlock> blocks = [];
        AddAssetGroup(
            blocks,
            "Word",
            assets,
            [
                new AssetLink("word_families", "Family", "word_families"),
                new AssetLink("etymology", "Origin", "etymologies"),
            ]);
        AddAssetGroup(
            blocks,
            "Collocations",
            assets,
            [
                new AssetLink("entry_collocations", "This Entry", "collocations"),
                new AssetLink("other_entries_collocations", "Other Entries", "collocations"),
                new AssetLink("corpus_collocations", "Corpus", "collocations"),
            ]);
        AddAssetGroup(
            blocks,
            "Thesaurus",
            assets,
            [
                new AssetLink("thesaurus", "Thesaurus", "thesaurus"),
                new AssetLink("activator", "Activator", "thesaurus"),
                new AssetLink("word_sets", "Word Set", "word_sets"),
            ]);
        AddAssetGroup(
            blocks,
            "Phrase Bank",
            assets,
            [
                new AssetLink("entry_phrases", "This Entry", "phrases"),
                new AssetLink("other_entries_phrases", "Other Entries", "phrases"),
            ]);
        AddAssetGroup(
            blocks,
            "Example Bank",
            assets,
            [
                new AssetLink("other_dictionary_examples", "Other Dicts", "examples"),
                new AssetLink("corpus_examples", "Corpus", "examples"),
            ]);
        AddExternalAssetGroup(blocks, root, webSearchSites, webSearchAssetBoxMode);

        return blocks.Count == 0 ? null : new DictionaryContainerBlock(blocks, DictionaryBlockStyle.AssetBox, null);
    }

    private sealed record AssetLink(string AssetKey, string LinkText, string TargetArchive);

    private static void AddAssetGroup(
        List<DictionaryBlock> blocks,
        string title,
        IReadOnlyDictionary<string, string> assets,
        IReadOnlyList<AssetLink> links)
    {
        List<DictionaryBlock> groupBlocks =
        [
            new DictionaryParagraphBlock(
                [new DictionaryTextInline(title, DictionaryTextStyle.AssetTitle)],
                DictionaryBlockStyle.AssetBox,
                null),
        ];

        foreach (AssetLink assetLink in links)
        {
            if (!assets.TryGetValue(assetLink.AssetKey, out string? name))
            {
                continue;
            }

            DictionaryLinkInline link = new(
                new DictionaryLinkTarget(DictionaryLinkTargetKind.DictionaryPath, "/" + assetLink.TargetArchive + "/" + name),
                [new DictionaryTextInline(assetLink.LinkText, DictionaryTextStyle.Normal)]);
            groupBlocks.Add(new DictionaryParagraphBlock([link], DictionaryBlockStyle.AssetBox, null));
        }

        if (groupBlocks.Count > 1)
        {
            blocks.Add(new DictionaryContainerBlock(groupBlocks, DictionaryBlockStyle.Normal, null));
        }
    }

    private static void AddExternalAssetGroup(
        List<DictionaryBlock> blocks,
        XElement root,
        IEnumerable<WebSearchSite>? webSearchSites,
        WebSearchAssetBoxMode webSearchAssetBoxMode)
    {
        if (!ShouldShowExternalAssetGroup(root, webSearchAssetBoxMode) || webSearchSites?.Count() == 0)
        {
            return;
        }

        string headword = GetText(root.Element("Head")?.Element("HWD")?.Element("BASE"));
        if (headword.Length == 0)
        {
            return;
        }

        AddExternalAssetGroup(
            blocks,
            "Web",
            WebSearchLinks.Create(headword, webSearchSites));
    }

    private static void AddExternalAssetGroup(
        List<DictionaryBlock> blocks,
        string title,
        IReadOnlyList<WebSearchLink> links)
    {
        List<DictionaryBlock> groupBlocks =
        [
            new DictionaryParagraphBlock(
                [new DictionaryTextInline(title, DictionaryTextStyle.AssetTitle)],
                DictionaryBlockStyle.AssetBox,
                null),
        ];

        foreach (WebSearchLink externalLink in links)
        {
            DictionaryLinkInline link = new(
                new DictionaryLinkTarget(DictionaryLinkTargetKind.External, externalLink.Url),
                [new DictionaryTextInline(externalLink.Title, DictionaryTextStyle.Normal)]);
            groupBlocks.Add(new DictionaryParagraphBlock([link], DictionaryBlockStyle.AssetBox, null));
        }

        blocks.Add(new DictionaryContainerBlock(groupBlocks, DictionaryBlockStyle.Normal, null));
    }

    private static bool ShouldShowExternalAssetGroup(XElement root, WebSearchAssetBoxMode mode)
    {
        return mode == WebSearchAssetBoxMode.AllEntries || IsNounEntry(root);
    }

    private static bool IsNounEntry(XElement root)
    {
        XElement? head = root.Element("Head");
        if (head is null)
        {
            return false;
        }

        return head.Elements("POS").Any(partOfSpeech => GetText(partOfSpeech) == "noun");
    }

    private static void FlushParagraph(
        List<DictionaryBlock> blocks,
        List<DictionaryInline> inlines,
        DictionaryBlockStyle style,
        string? anchor,
        List<string> anchorAliases)
    {
        IReadOnlyList<DictionaryInline> compact = CompactInlines(inlines);
        if (compact.Count > 0)
        {
            blocks.Add(new DictionaryParagraphBlock(compact, style, anchor)
            {
                AnchorAliases = anchorAliases.Distinct(StringComparer.Ordinal).ToArray(),
            });
        }

        inlines.Clear();
        anchorAliases.Clear();
    }

    private static IReadOnlyList<DictionaryInline> CompactInlines(IReadOnlyList<DictionaryInline> inlines)
    {
        List<DictionaryInline> compact = [];
        foreach (DictionaryInline inline in inlines)
        {
            if (inline is DictionaryTextInline text && text.Text.Length == 0)
            {
                continue;
            }

            if (inline is DictionaryTextInline currentText
                && compact.LastOrDefault() is DictionaryTextInline previousText)
            {
                string value = currentText.Text;
                if (previousText.Text.EndsWith(' ') && value.StartsWith(' '))
                {
                    value = value.TrimStart();
                }
                else if (!previousText.Text.EndsWith(' ')
                    && !value.StartsWith(' ')
                    && currentText.Style != DictionaryTextStyle.HomonymNumber
                    && ShouldInsertSpaceBetween(previousText.Text, value))
                {
                    if (UsesDecoratedBackground(currentText.Style))
                    {
                        compact.Add(new DictionaryTextInline(" ", DictionaryTextStyle.Normal));
                    }
                    else
                    {
                        value = " " + value;
                    }
                }

                compact.Add(currentText with { Text = value });
                continue;
            }

            compact.Add(inline);
        }

        return compact;
    }

    private static bool UsesDecoratedBackground(DictionaryTextStyle style)
    {
        return style is DictionaryTextStyle.Signpost or DictionaryTextStyle.RelationTag or DictionaryTextStyle.FrequencyTag;
    }

    private static void AddText(List<DictionaryInline> inlines, string? text, DictionaryTextStyle style, string? anchor = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string normalized = NormalizeWhitespace(text);
        if (normalized.Length > 0)
        {
            bool leadingSpace = char.IsWhiteSpace(text[0]);
            bool trailingSpace = char.IsWhiteSpace(text[^1]);
            string value = normalized;
            if (leadingSpace && ShouldPreserveLeadingSpace(value, inlines))
            {
                value = " " + value;
            }

            if (trailingSpace)
            {
                value += " ";
            }

            if (style is DictionaryTextStyle.Signpost or DictionaryTextStyle.RelationTag)
            {
                value = value.ToUpperInvariant();
            }

            inlines.Add(new DictionaryTextInline(value, style, anchor));
        }
    }

    private static string GetText(XElement? element)
    {
        return element is null ? string.Empty : NormalizeWhitespace(string.Concat(element.DescendantNodes().OfType<XText>().Select(text => text.Value)));
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace('\u2027', '\u00b7');
    }

    private static bool StartsWithPunctuation(string value)
    {
        return value.Length > 0 && (char.IsPunctuation(value[0]) || value[0] == ':' || value[0] == ';');
    }

    private static bool ShouldPreserveLeadingSpace(string value, IReadOnlyList<DictionaryInline> inlines)
    {
        if (inlines.Count == 0)
        {
            return value[0] is '/' or '(' or '[' or '{';
        }

        if (inlines.LastOrDefault() is DictionaryTextInline previousText
            && previousText.Text.Length > 0
            && IsOpeningDelimiter(previousText.Text[^1]))
        {
            return false;
        }

        return !StartsWithPunctuation(value) || value[0] is '/' or '(' or '[' or '{';
    }

    private static bool ShouldInsertSpaceBetween(string previous, string current)
    {
        char previousChar = previous[^1];
        char currentChar = current[0];
        if (IsOpeningDelimiter(previousChar) || IsClosingDelimiter(currentChar))
        {
            return false;
        }

        if (previousChar == '/' && previous.Trim() == "/" && char.IsWhiteSpace(previous[0]))
        {
            return false;
        }

        if (currentChar == '/')
        {
            return false;
        }

        return !StartsWithPunctuation(current) || IsOpeningDelimiter(currentChar);
    }

    private static bool IsOpeningDelimiter(char value)
    {
        return value is '(' or '[' or '{' or '‘' or '“';
    }

    private static bool IsClosingDelimiter(char value)
    {
        return value is ')' or ']' or '}' or '’' or '”';
    }

    private static string? GetAnchor(XElement element)
    {
        string? id = (string?)element.Attribute("id");
        return string.IsNullOrWhiteSpace(id) ? null : IdmArchive.ShortenId(id);
    }

    private static IEnumerable<string> GetBlockAnchors(XElement element)
    {
        foreach (XElement descendant in element.DescendantsAndSelf())
        {
            if (!IsBlockElement(descendant))
            {
                continue;
            }

            string? anchor = GetAnchor(descendant);
            if (!string.IsNullOrWhiteSpace(anchor))
            {
                yield return anchor;
            }
        }
    }
}
