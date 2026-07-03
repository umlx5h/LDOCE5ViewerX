using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Extracts searchable records from LDOCE XML documents for index creation.
/// </summary>
public static partial class SearchItemExtractor
{
    private static readonly HashSet<string> ExcludedTextTags = ["span", "OBJECT", "GLOSS"];
    private const string RightArrow = "\u2192"; // &rarr;
    private const string EmDash = "\u2014"; // &mdash;
    private const string Ellipsis = "\u2026"; // &hellip;

    /// <summary>
    /// Extracts entry items and inflection variations from an <c>fs</c> entry XML document.
    /// </summary>
    /// <param name="entryData">Raw entry XML bytes.</param>
    /// <returns>Search items plus variation mapping.</returns>
    public static (IReadOnlyList<SearchIndexItem> Items, IReadOnlyDictionary<string, IReadOnlyCollection<string>> Variations)
        ExtractEntryItems(byte[] entryData)
    {
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(entryData), LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Entry XML has no root element.");
        XElement head = root.Element("Head") ?? throw new InvalidDataException("Entry XML has no Head element.");
        string rootId = IdmArchive.ShortenId((string?)root.Attribute("id") ?? string.Empty);
        string entryPath = "/fs/" + rootId;

        XElement? hyphenation = head.Element("HYPHENATION");
        int syllableCount = hyphenation is null ? 1 : GetText(hyphenation).Count(c => c == '\u2027') + 1;
        bool isFrequent = head.Element("FREQ") is not null;
        string headwordPlain = GetText(head.Element("HWD")?.Element("BASE"));

        XElement[] mainGramElements = head.Descendants("GRAM").ToArray();
        XElement[] headPosElements = head.Descendants("POS").ToArray();
        HashSet<string> headPartsOfSpeech = headPosElements.Select(e => GetText(e).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        HashSet<string> incorrectInflections = GetIncorrectInflections(
            headwordPlain,
            headPosElements,
            mainGramElements,
            root.Elements("Sense").SelectMany(e => e.Descendants("GRAM")),
            syllableCount).ToHashSet(StringComparer.Ordinal);

        HashSet<string> mainGrammar = mainGramElements.Select(e => GetText(e).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        bool isHeadwordNoun = headPartsOfSpeech.Contains("noun");
        bool isHeadwordAdjective = headPartsOfSpeech.Contains("adjective");
        bool isUncountable = isHeadwordNoun && mainGrammar.Any(s => UncountableRegex().IsMatch(s) && !CountableRegex().IsMatch(s));

        bool isAmerican = false;
        bool isBritish = false;
        if (head.Element("AmEVariant") is null && head.Element("BrEVariant") is null)
        {
            foreach (string geography in head.Elements("GEO").Select(GetText))
            {
                if (geography.Contains("British", StringComparison.Ordinal))
                {
                    isBritish = true;
                }
                else if (geography.Contains("American", StringComparison.Ordinal))
                {
                    isAmerican = true;
                }
            }
        }

        string headwordLabel = MakeHeadwordLabel(head, isFrequent);
        List<SearchIndexItem> items = [];
        SearchIndexItem headword = GetHeadwordItem(head, entryPath, headwordPlain, headwordLabel, isUncountable, isBritish, isAmerican, isHeadwordAdjective);
        items.Add(headword);

        string wrappedHeadwordLabel = headword.Label;
        string wrappedHeadwordPlain = headword.Content;
        HashSet<string> inflections = [];
        foreach (SearchIndexItem item in GetHeadwordVariants(head, entryPath, headwordPlain, headwordLabel, incorrectInflections, isHeadwordAdjective))
        {
            items.Add(item);
            inflections.Add(item.Content);
        }

        IReadOnlyDictionary<string, IReadOnlyCollection<string>> variations = MakeVariations(wrappedHeadwordPlain, inflections);

        foreach (XElement sense in root.Descendants("Sense"))
        {
            items.AddRange(GetSenseItems(sense, rootId, wrappedHeadwordLabel, headwordLabel, wrappedHeadwordPlain));
        }

        foreach (XElement runOn in root.Descendants("RunOn"))
        {
            items.AddRange(GetRunOnItems(runOn, rootId, headwordLabel, syllableCount, isHeadwordAdjective));
        }

        foreach (XElement phrasalVerb in root.Descendants("PhrVbEntry"))
        {
            items.AddRange(GetPhrasalVerbItems(phrasalVerb, rootId));
        }

        foreach (XElement example in root.Descendants("EXAMPLE"))
        {
            items.AddRange(GetExampleItems(example, rootId, wrappedHeadwordLabel, headwordLabel, wrappedHeadwordPlain));
        }

        foreach (XElement lexicalUnit in root.Descendants("LEXUNIT"))
        {
            items.AddRange(GetLexicalUnitItems(lexicalUnit, rootId, headwordLabel));
        }

        foreach (XElement propFormPrep in root.Descendants("PROPFORMPREP"))
        {
            items.AddRange(GetSimplePhraseItems(propFormPrep, rootId, headwordLabel));
        }

        foreach (XElement propForm in root.Descendants("PROPFORM"))
        {
            items.AddRange(GetSimplePhraseItems(propForm, rootId, headwordLabel));
        }

        foreach (XElement collocate in root.Descendants("Collocate"))
        {
            items.AddRange(GetCollocateItems(collocate, rootId, wrappedHeadwordLabel, headwordLabel, wrappedHeadwordPlain));
        }

        foreach (XElement exponent in root.Descendants("Exponent"))
        {
            items.AddRange(GetExponentItems(exponent, rootId, wrappedHeadwordLabel, wrappedHeadwordPlain));
        }

        foreach (XElement collocation in root.Descendants("COLLO"))
        {
            items.AddRange(GetSimplePhraseItems(collocation, rootId, headwordLabel));
        }

        foreach (XElement collocation in root.Descendants("COLLOC"))
        {
            items.AddRange(GetCollocationItems(collocation, rootId, headwordLabel));
        }

        return (items, variations);
    }

    /// <summary>
    /// Extracts activator concept and exponent items from activator XML documents.
    /// </summary>
    /// <param name="conceptData">Activator concept XML bytes.</param>
    /// <param name="sectionExponents">Map of section id to exponent ids and display text.</param>
    /// <returns>Activator search items.</returns>
    public static IReadOnlyList<SearchIndexItem> ExtractActivatorItems(
        byte[] conceptData,
        IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Text)>> sectionExponents)
    {
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(conceptData), LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Activator concept XML has no root element.");
        string conceptId = (string?)root.Attribute("id") ?? string.Empty;
        string heading = root.Element("HWD")?.Value ?? string.Empty;
        string firstSectionId = (string?)root.Elements("Section").FirstOrDefault()?.Attribute("id") ?? string.Empty;

        List<SearchIndexItem> items = [];
        foreach (string hwd in heading.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            items.Add(new SearchIndexItem(
                "ac",
                $"<a><c>{Html(hwd)}</c></a>",
                $"/activator/{conceptId}/{firstSectionId}",
                hwd,
                hwd,
                string.Empty,
                50));
        }

        int sectionNumber = 0;
        foreach (XElement section in root.Elements("Section"))
        {
            string sectionId = (string?)section.Attribute("id") ?? string.Empty;
            if (sectionExponents.TryGetValue(sectionId, out IReadOnlyList<(string Id, string Text)>? exponents))
            {
                foreach ((string exponentId, string text) in exponents)
                {
                    items.Add(new SearchIndexItem(
                        "ae",
                        $"<a><e>{Html(text)}</e> (<c>{Html(heading)}<s>{sectionNumber + 1}</s></c>)</a>",
                        $"/activator/{conceptId}/{sectionId}#{exponentId}",
                        text,
                        text,
                        string.Empty,
                        51));
                }
            }

            sectionNumber++;
        }

        return items;
    }

    /// <summary>
    /// Extracts exponent ids and phrases from an activator section XML document.
    /// </summary>
    /// <param name="sectionData">Raw section XML bytes.</param>
    /// <returns>Section id plus exponent pairs.</returns>
    public static (string SectionId, IReadOnlyList<(string Id, string Text)> Exponents) ExtractActivatorSection(byte[] sectionData)
    {
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(sectionData), LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Activator section XML has no root element.");
        string sectionId = (string?)root.Attribute("id") ?? string.Empty;
        List<(string Id, string Text)> exponents = [];
        foreach (XElement exponent in root.Elements("Exponent"))
        {
            string exponentId = (string?)exponent.Attribute("id") ?? string.Empty;
            string text = (exponent.Element("EXP")?.Value ?? string.Empty).Trim();
            exponents.Add((exponentId, text));
        }

        return (sectionId, exponents);
    }

    /// <summary>
    /// Creates the primary headword record for an entry.
    /// </summary>
    private static SearchIndexItem GetHeadwordItem(
        XElement head,
        string entryPath,
        string plain,
        string headwordLabel,
        bool isUncountable,
        bool isBritish,
        bool isAmerican,
        bool isHeadwordAdjective)
    {
        string filter = GetFilter(head.Element("HWD"), isHeadwordAdjective);
        if (isUncountable)
        {
            filter += " u1";
        }

        if (isBritish)
        {
            filter += " u2";
        }

        if (isAmerican)
        {
            filter += " u3";
        }

        return new SearchIndexItem("hm", $"<h>{headwordLabel}</h>", entryPath, plain, plain, filter, 1);
    }

    /// <summary>
    /// Extracts inflected, lexical, orthographic, and abbreviation variants of the headword.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetHeadwordVariants(
        XElement head,
        string entryPath,
        string headwordPlain,
        string headwordLabel,
        IReadOnlySet<string> incorrectInflections,
        bool isHeadwordAdjective)
    {
        XElement? hwd = head.Element("HWD");
        string filter = GetFilter(hwd, isHeadwordAdjective);
        foreach (XElement inflection in hwd?.Elements("INFLX") ?? [])
        {
            string plain = GetText(inflection);
            if (plain == headwordPlain || incorrectInflections.Contains(plain))
            {
                continue;
            }

            yield return new SearchIndexItem("hv", $"<h><v>{Html(plain)}</v> {RightArrow} {headwordLabel}</h>", entryPath, plain, plain, filter, 2);
        }

        IEnumerable<XElement> variants = head.Descendants("LEXVAR").Concat(head.Descendants("ORTHVAR"));
        foreach (XElement variant in variants)
        {
            string? variantId = (string?)variant.Attribute("id");
            if (variantId is null)
            {
                continue;
            }

            string variantPath = $"{entryPath}#{IdmArchive.ShortenId(variantId)}";
            string variantFilter = GetFilter(variant, isHeadwordAdjective);
            foreach (string plain in variant.Elements("INFLX").Select(GetText).ToHashSet(StringComparer.Ordinal))
            {
                if (incorrectInflections.Contains(plain))
                {
                    continue;
                }

                yield return new SearchIndexItem("hv", $"<h><v>{Html(plain)}</v> {RightArrow} {headwordLabel}</h>", variantPath, plain, plain, variantFilter, 2);
            }
        }

        foreach (XElement abbreviation in head.Descendants("ABBR"))
        {
            string plain = GetText(abbreviation);
            yield return new SearchIndexItem("hv", $"<h><v>{Html(plain)}</v> {RightArrow} {headwordLabel}</h>", entryPath, plain, plain, string.Empty, 2);
        }
    }

    /// <summary>
    /// Extracts phrasal-verb headword items.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetPhrasalVerbItems(XElement phrasalVerb, string rootId)
    {
        string? id = (string?)phrasalVerb.Attribute("id");
        XElement? phrasalVerbHeadword = phrasalVerb.Element("Head")?.Element("PHRVBHWD");
        string plain = GetText(phrasalVerbHeadword);
        string filter = GetFilter(phrasalVerbHeadword, isHeadwordAdjective: true);
        yield return new SearchIndexItem(
            "hp",
            $"<h><pv>{Html(plain)}</pv> <p>phrasal verb</p></h>",
            $"/fs/{rootId}#{IdmArchive.ShortenId(id ?? string.Empty)}",
            plain,
            plain,
            filter,
            1);
    }

    /// <summary>
    /// Extracts derived run-on entries and their variants.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetRunOnItems(
        XElement runOn,
        string rootId,
        string headwordLabel,
        int syllableCount,
        bool isHeadwordAdjective)
    {
        XElement? derivation = runOn.Element("DERIV");
        if (derivation is null)
        {
            yield break;
        }

        string path = $"/fs/{rootId}#{IdmArchive.ShortenId((string?)derivation.Attribute("id") ?? string.Empty)}";
        XElement[] posElements = runOn.Descendants("POS").ToArray();
        string[] partsOfSpeech = posElements.Select(e => GetText(e).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        HashSet<string> partOfSpeechSet = partsOfSpeech.ToHashSet(StringComparer.Ordinal);
        string filter = GetFilter(derivation, isHeadwordAdjective, partOfSpeechSet);
        string plain = RemoveStressMarks(GetText(derivation.Element("BASE")));
        string label = $"<n>{Html(plain)}</n> <p>{Html(string.Join(", ", partsOfSpeech))}</p>";
        yield return new SearchIndexItem("hm", $"<h>{label}</h>", path, plain, plain, filter, 1);

        HashSet<string> incorrect = GetIncorrectInflections(plain, runOn.Elements("POS"), runOn.Elements("GRAM"), [], syllableCount)
            .ToHashSet(StringComparer.Ordinal);
        foreach (XElement inflection in derivation.Elements("INFLX"))
        {
            string inflectionPlain = GetText(inflection);
            if (inflectionPlain == plain || incorrect.Contains(inflectionPlain))
            {
                continue;
            }

            // fix typo closing <v> found in the original implementation
            yield return new SearchIndexItem("hv", $"<h><v>{Html(inflectionPlain)}</v> {RightArrow} {label}</h>", path, inflectionPlain, inflectionPlain, filter, 1);
        }
    }

    /// <summary>
    /// Extracts lexical-unit phrase items.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetLexicalUnitItems(XElement element, string rootId, string headwordLabel)
    {
        string plain = GetText(element);
        yield return new SearchIndexItem(
            "pl",
            $"<l><o>{Html(plain)}</o> ({headwordLabel})</l>",
            PathFor(rootId, element),
            plain,
            plain,
            GetFilter(element, isHeadwordAdjective: true),
            9);
    }

    /// <summary>
    /// Extracts simple phrase and collocation items.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetSimplePhraseItems(XElement element, string rootId, string headwordLabel)
    {
        string plain = GetText(element);
        yield return new SearchIndexItem(
            "p",
            $"<c><o>{plain}</o> ({headwordLabel})</c>",
            PathFor(rootId, element),
            plain,
            plain,
            GetFilter(element, isHeadwordAdjective: true),
            10);
    }

    /// <summary>
    /// Extracts collocation phrase items while removing a leading indefinite article from the search key.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetCollocationItems(XElement element, string rootId, string headwordLabel)
    {
        string plain = RemoveArticle(GetText(element));
        yield return new SearchIndexItem(
            "p",
            $"<c><o>{plain}</o> ({headwordLabel})</c>",
            PathFor(rootId, element),
            plain,
            plain,
            GetFilter(element, isHeadwordAdjective: true),
            10);
    }

    /// <summary>
    /// Extracts examples and phrase items from a collocate block.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetCollocateItems(
        XElement collocate,
        string rootId,
        string wrappedHeadwordLabel,
        string headwordLabel,
        string headwordPlain)
    {
        string? id = (string?)collocate.Attribute("id");
        if (id is null)
        {
            yield break;
        }

        IEnumerable<XElement> titleParts = collocate.Elements("COLLOC")
            .Concat(collocate.Descendants("LEXVAR"))
            .Concat(collocate.Descendants("ORTHVAR"));
        string title = string.Join(", ", titleParts.Select(e => $"<b>{Html(GetText(e))}</b>"));
        string path = $"/fs/{rootId}#{IdmArchive.ShortenId(id)}";

        foreach (XElement example in collocate.Elements("COLLEXA"))
        {
            string plain = GetText2(example.Element("BASE"));
            string filter = GetFilter(example, isHeadwordAdjective: true);
            yield return new SearchIndexItem("e", $"{wrappedHeadwordLabel} {EmDash} {title}", path, plain, headwordPlain, filter, 20);
        }

        IEnumerable<XElement> variants = collocate.Descendants("LEXVAR").Concat(collocate.Descendants("ORTHVAR"));
        foreach (XElement variant in variants)
        {
            string? variantId = (string?)variant.Attribute("id");
            if (variantId is null)
            {
                continue;
            }

            string plain = GetText(variant);
            yield return new SearchIndexItem(
                "p",
                $"<c><o>{plain}</o> ({headwordLabel})</c>",
                $"/fs/{rootId}#{IdmArchive.ShortenId(variantId)}",
                plain,
                plain,
                string.Empty,
                11);
        }
    }

    /// <summary>
    /// Extracts thesaurus exponent examples and definitions.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetExponentItems(XElement exponent, string rootId, string wrappedHeadwordLabel, string headwordPlain)
    {
        string? id = (string?)exponent.Attribute("id");
        if (id is null)
        {
            yield break;
        }

        IEnumerable<XElement> titleParts = exponent.Elements("EXP")
            .Concat(exponent.Descendants("LEXVAR"))
            .Concat(exponent.Descendants("ORTHVAR"));
        string title = string.Join(", ", titleParts.Select(e => $"<b>{Html(GetText(e))}</b>"));
        string path = $"/fs/{rootId}#{IdmArchive.ShortenId(id)}";

        foreach (XElement example in exponent.Descendants("THESEXA"))
        {
            string text = GetText2(example.Element("BASE"));
            string filter = GetFilter(example, isHeadwordAdjective: true);
            yield return new SearchIndexItem("e", wrappedHeadwordLabel, path, text, headwordPlain, filter, 20);
        }

        foreach (XElement definition in exponent.Descendants("DEF"))
        {
            string text = GetText(definition);
            string filter = GetFilter(definition, isHeadwordAdjective: true);
            yield return new SearchIndexItem("d", $"{wrappedHeadwordLabel} {EmDash} {title}", path, text, headwordPlain, filter, 30);
        }
    }

    /// <summary>
    /// Extracts normal examples and collocations embedded inside examples.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetExampleItems(
        XElement example,
        string rootId,
        string wrappedHeadwordLabel,
        string headwordLabel,
        string headwordPlain)
    {
        string path = PathFor(rootId, example);
        string text = GetText2(example.Element("BASE"));
        yield return new SearchIndexItem("e", wrappedHeadwordLabel, path, text, headwordPlain, GetFilter(example, isHeadwordAdjective: true), 20);

        string[] collocations = example.Descendants("COLLOINEXA").Select(GetText).ToArray();
        if (collocations.Length == 0)
        {
            yield break;
        }

        string plain = string.Join(" ", collocations);
        string labelText = string.Join($" {Ellipsis} ", collocations.Select(Html));
        yield return new SearchIndexItem("p", $"<c><o>{labelText}</o> ({headwordLabel})</c>", path, plain, plain, string.Empty, 15);
    }

    /// <summary>
    /// Extracts sense definitions and phrase variants within senses.
    /// </summary>
    private static IEnumerable<SearchIndexItem> GetSenseItems(
        XElement sense,
        string rootId,
        string wrappedHeadwordLabel,
        string headwordLabel,
        string headwordPlain)
    {
        string sensePath = PathFor(rootId, sense);
        foreach (XElement definition in sense.Descendants("DEF"))
        {
            string text = GetText(definition);
            string filter = GetFilter(definition, isHeadwordAdjective: true);
            yield return new SearchIndexItem("d", wrappedHeadwordLabel, sensePath, text, headwordPlain, filter, 30);
        }

        IEnumerable<XElement> variants = sense.Descendants("LEXVAR").Concat(sense.Descendants("ORTHVAR"));
        foreach (XElement variant in variants)
        {
            string plain = RemoveStressMarks(GetText(variant).Replace("\u00b7", string.Empty, StringComparison.Ordinal));
            yield return new SearchIndexItem(
                "pl",
                $"<l><o>{Html(plain)}</o> ({headwordLabel})</l>",
                PathFor(rootId, variant),
                plain,
                plain,
                string.Empty,
                11);
        }
    }

    /// <summary>
    /// Builds the display label for a headword in the index markup format.
    /// </summary>
    private static string MakeHeadwordLabel(XElement head, bool isFrequent)
    {
        XElement? hwd = head.Element("HWD");
        string label = Html(GetText(hwd?.Element("BASE")));
        string homonymNumber = GetText(head.Element("HOMNUM"));
        if (homonymNumber.Length > 0)
        {
            label += $"<s>{Html(homonymNumber)}</s>";
        }

        label = isFrequent ? $"<f>{label}</f>" : $"<n>{label}</n>";
        string[] partsOfSpeech = head.Elements("POS").Select(GetText).Where(s => s.Length > 0).ToArray();
        if (partsOfSpeech.Length > 0)
        {
            label += $" <p>{Html(string.Join(", ", partsOfSpeech))}</p>";
        }

        return label;
    }

    /// <summary>
    /// Creates the bidirectional word-variation map used by full-text term expansion.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> MakeVariations(string headword, HashSet<string> inflections)
    {
        if (headword.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1)
        {
            return new Dictionary<string, IReadOnlyCollection<string>>();
        }

        HashSet<string> all = [headword.ToLowerInvariant(), .. inflections.Select(s => s.ToLowerInvariant())];
        Dictionary<string, IReadOnlyCollection<string>> variations = [];
        foreach (string word in all)
        {
            variations[word] = all.Where(candidate => !string.Equals(candidate, word, StringComparison.Ordinal)).ToArray();
        }

        return variations;
    }

    /// <summary>
    /// Builds impossible regular inflections to exclude from the variant index.
    /// </summary>
    private static IEnumerable<string> GetIncorrectInflections(
        string word,
        IEnumerable<XElement> posElements,
        IEnumerable<XElement> mainGrammarElements,
        IEnumerable<XElement> subGrammarElements,
        int syllableCount)
    {
        HashSet<string> partsOfSpeech = posElements.Select(e => GetText(e).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        HashSet<string> mainGrammar = mainGrammarElements.Select(e => GetText(e).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        HashSet<string> grammar = mainGrammar.Concat(subGrammarElements.Select(e => GetText(e).ToLowerInvariant())).ToHashSet(StringComparer.Ordinal);

        List<string> incorrect = [];
        foreach (string partOfSpeech in partsOfSpeech)
        {
            if (partOfSpeech == "noun")
            {
                incorrect.AddRange(GetIncorrectNounInflections(word, grammar));
            }
            else if (partOfSpeech == "adjective")
            {
                incorrect.AddRange(GetIncorrectAdjectiveInflections(word, grammar, syllableCount));
            }
        }

        return incorrect;
    }

    /// <summary>
    /// Builds regular plural spellings that should not be indexed for uncountable nouns.
    /// </summary>
    private static IEnumerable<string> GetIncorrectNounInflections(string word, IEnumerable<string> grammar)
    {
        if (grammar.Any(s => CountableRegex().IsMatch(s)))
        {
            return [];
        }

        if (word.EndsWith("y", StringComparison.Ordinal))
        {
            return [word + "s", word[..^1] + "ies"];
        }

        if (word.EndsWith("f", StringComparison.Ordinal))
        {
            return [word[..^1] + "ves"];
        }

        if (word.EndsWith("fe", StringComparison.Ordinal))
        {
            return [word[..^2] + "ves"];
        }

        return [word + "s", word + "es"];
    }

    /// <summary>
    /// Builds regular comparative spellings that should not be indexed for non-comparable adjectives.
    /// </summary>
    private static IEnumerable<string> GetIncorrectAdjectiveInflections(string word, IEnumerable<string> grammar, int syllableCount)
    {
        string[] MakeComparative()
        {
            if (word.EndsWith("e", StringComparison.Ordinal))
            {
                return [word + "r", word + "st"];
            }

            if (word.EndsWith("y", StringComparison.Ordinal))
            {
                string stem = word[..^1];
                return [stem + "ier", stem + "iest"];
            }

            return [word + "er", word + "est"];
        }

        if (grammar.Any(s => s.Contains("no comparative", StringComparison.Ordinal)))
        {
            return MakeComparative();
        }

        if (syllableCount >= 3)
        {
            return MakeComparative();
        }

        if (syllableCount >= 2
            && !word.EndsWith("y", StringComparison.Ordinal)
            && !word.EndsWith("le", StringComparison.Ordinal)
            && !word.EndsWith("er", StringComparison.Ordinal))
        {
            return MakeComparative();
        }

        return [];
    }

    /// <summary>
    /// Gets the advanced-search filter value from an XML element.
    /// </summary>
    private static string GetFilter(XElement? element, bool isHeadwordAdjective, IReadOnlySet<string>? localPartsOfSpeech = null)
    {
        string? value = (string?)element?.Attribute("as_filter");
        if (value is null)
        {
            return string.Empty;
        }

        HashSet<string> filters = value.Replace("|", string.Empty, StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (localPartsOfSpeech is null)
        {
            if (!isHeadwordAdjective)
            {
                filters.Remove("334");
            }
        }
        else if (!localPartsOfSpeech.Contains("adjective"))
        {
            filters.Remove("334");
        }

        return string.Join(" ", filters);
    }

    /// <summary>
    /// Extracts searchable text while skipping tags ignored by the Python extractor.
    /// </summary>
    private static string GetText(XElement? element)
    {
        if (element is null || ExcludedTextTags.Contains(element.Name.LocalName))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        AppendText(element, builder);
        return builder.ToString().Trim();
    }

    /// <summary>
    /// Appends raw XML text for <see cref="GetText"/> recursively.
    /// </summary>
    private static void AppendText(XElement element, StringBuilder builder)
    {
        if (ExcludedTextTags.Contains(element.Name.LocalName))
        {
            return;
        }

        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child)
            {
                AppendText(child, builder);
            }
        }
    }

    /// <summary>
    /// Extracts example text while preserving collocations embedded in examples.
    /// </summary>
    private static string GetText2(XElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        List<string> parts = [];
        bool seenElement = false;
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                if (!seenElement || text.Value.Length > 0)
                {
                    parts.Add(text.Value);
                }
            }
            else if (node is XElement child)
            {
                seenElement = true;
                if (child.Name.LocalName == "COLLOINEXA")
                {
                    parts.Add(GetText(child));
                }
            }
        }

        return SpaceRegex().Replace(string.Join(" ", parts), " ").Trim();
    }

    /// <summary>
    /// Builds an entry-local path from an item id.
    /// </summary>
    private static string PathFor(string rootId, XElement element)
    {
        return $"/fs/{rootId}#{IdmArchive.ShortenId((string?)element.Attribute("id") ?? string.Empty)}";
    }

    /// <summary>
    /// Removes leading indefinite articles from collocation search keys.
    /// </summary>
    private static string RemoveArticle(string value)
    {
        if (value.StartsWith("a ", StringComparison.Ordinal))
        {
            return value[2..];
        }

        if (value.StartsWith("an ", StringComparison.Ordinal))
        {
            return value[3..];
        }

        return value;
    }

    /// <summary>
    /// Removes dictionary stress marks from generated headword variants.
    /// </summary>
    private static string RemoveStressMarks(string value)
    {
        return value.Replace("\u02c8", string.Empty, StringComparison.Ordinal)
            .Replace("\u02cc", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Encodes text for index markup labels.
    /// </summary>
    private static string Html(string value)
    {
        Debug.Assert(!value.ContainsAny(['<', '>', '"']));
        return value;

        //return value
        //    .Replace("&", "&amp;", StringComparison.Ordinal) // only required
        //    .Replace("<", "&lt;", StringComparison.Ordinal)
        //    .Replace(">", "&gt;", StringComparison.Ordinal)
        //    .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"(\bcountable\b|\bc\b|\b(often|usually)\s+plural\b)")]
    private static partial Regex CountableRegex();

    [GeneratedRegex(@"(\buncountable\b)")]
    private static partial Regex UncountableRegex();
}
