using System.Collections.Generic;

namespace LDOCE5ViewerX.Models;

/// <summary>
/// Rich dictionary page loaded from local LDOCE data.
/// </summary>
/// <param name="Title">Page title.</param>
/// <param name="Path">Canonical dictionary path without a fragment.</param>
/// <param name="Anchor">Optional in-page anchor to scroll to.</param>
/// <param name="Document">Rich document content.</param>
public sealed record DictionaryContentPage(
    string Title,
    string Path,
    string? Anchor,
    DictionaryDocument Document);

/// <summary>
/// Root container for rich dictionary blocks.
/// </summary>
/// <param name="Blocks">Document blocks in display order.</param>
public sealed record DictionaryDocument(IReadOnlyList<DictionaryBlock> Blocks)
{
    /// <summary>
    /// Creates a document containing one paragraph of plain text.
    /// </summary>
    /// <param name="text">Text to display.</param>
    /// <returns>A paragraph-only document.</returns>
    public static DictionaryDocument FromPlainText(string text)
    {
        return new DictionaryDocument(
        [
            new DictionaryParagraphBlock(
                [new DictionaryTextInline(text, DictionaryTextStyle.Normal)],
                DictionaryBlockStyle.Normal,
                null),
        ]);
    }
}

/// <summary>
/// Base type for rich dictionary document blocks.
/// </summary>
/// <param name="Style">Visual role of the block.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id.</param>
public abstract record DictionaryBlock(DictionaryBlockStyle Style, string? Anchor)
{
    /// <summary>
    /// Additional XML ids inside this rendered block that should scroll to and highlight the block.
    /// </summary>
    public IReadOnlyList<string> AnchorAliases { get; set; } = [];
}

/// <summary>
/// Text paragraph with rich inline content.
/// </summary>
/// <param name="Inlines">Inline content.</param>
/// <param name="Style">Visual role of the paragraph.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id.</param>
public sealed record DictionaryParagraphBlock(
    IReadOnlyList<DictionaryInline> Inlines,
    DictionaryBlockStyle Style,
    string? Anchor)
    : DictionaryBlock(Style, Anchor);

/// <summary>
/// Heading block with rich inline content.
/// </summary>
/// <param name="Level">Heading level starting at one.</param>
/// <param name="Inlines">Inline content.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id.</param>
public sealed record DictionaryHeadingBlock(
    int Level,
    IReadOnlyList<DictionaryInline> Inlines,
    string? Anchor)
    : DictionaryBlock(DictionaryBlockStyle.Heading, Anchor);

/// <summary>
/// Nested visual group, such as a sense, asset box, or examples list.
/// </summary>
/// <param name="Blocks">Child blocks.</param>
/// <param name="Style">Visual role of the group.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id.</param>
public sealed record DictionaryContainerBlock(
    IReadOnlyList<DictionaryBlock> Blocks,
    DictionaryBlockStyle Style,
    string? Anchor)
    : DictionaryBlock(Style, Anchor);

/// <summary>
/// Standalone dictionary image.
/// </summary>
/// <param name="Resource">Image resource location.</param>
/// <param name="Caption">Optional image caption.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id.</param>
public sealed record DictionaryImageBlock(
    DictionaryResourceRef Resource,
    string? Caption,
    string? Anchor)
    : DictionaryBlock(DictionaryBlockStyle.Image, Anchor)
{
    /// <summary>Optional full-size image target.</summary>
    public DictionaryLinkTarget? Target { get; init; }
}

/// <summary>
/// Base type for rich text inlines.
/// </summary>
public abstract record DictionaryInline;

/// <summary>
/// Plain text with a visual role.
/// </summary>
/// <param name="Text">Text value.</param>
/// <param name="Style">Visual role of the text.</param>
/// <param name="Anchor">Optional shortened LDOCE anchor id for this inline range.</param>
public sealed record DictionaryTextInline(string Text, DictionaryTextStyle Style, string? Anchor = null) : DictionaryInline;

/// <summary>
/// Explicit line break.
/// </summary>
public sealed record DictionaryLineBreakInline : DictionaryInline;

/// <summary>
/// Link to another dictionary location, lookup query, or external URL.
/// </summary>
/// <param name="Target">Link target.</param>
/// <param name="Inlines">Visible link content.</param>
public sealed record DictionaryLinkInline(
    DictionaryLinkTarget Target,
    IReadOnlyList<DictionaryInline> Inlines)
    : DictionaryInline;

/// <summary>
/// Pronunciation or example audio button.
/// </summary>
/// <param name="Resource">Audio resource location.</param>
/// <param name="Title">Accessible button title.</param>
public sealed record DictionaryAudioInline(DictionaryResourceRef Resource, string Title) : DictionaryInline;

/// <summary>
/// Small inline image, such as an illustration thumbnail.
/// </summary>
/// <param name="Resource">Image resource location.</param>
/// <param name="Target">Optional full-size image target.</param>
public sealed record DictionaryImageInline(
    DictionaryResourceRef Resource,
    DictionaryLinkTarget? Target)
    : DictionaryInline;

/// <summary>
/// Link destination used by rich dictionary content.
/// </summary>
/// <param name="Kind">Target kind.</param>
/// <param name="Value">Path, query, or URL.</param>
public sealed record DictionaryLinkTarget(DictionaryLinkTargetKind Kind, string Value);

/// <summary>
/// Dictionary archive resource reference.
/// </summary>
/// <param name="Archive">Archive name.</param>
/// <param name="Name">Archive item name.</param>
/// <param name="MediaType">Expected media type.</param>
public sealed record DictionaryResourceRef(string Archive, string Name, string MediaType);

/// <summary>
/// Visual role for document blocks.
/// </summary>
public enum DictionaryBlockStyle
{
    /// <summary>Normal paragraph.</summary>
    Normal,

    /// <summary>Document heading.</summary>
    Heading,

    /// <summary>Headword header.</summary>
    EntryHead,

    /// <summary>Definition or sense block.</summary>
    Sense,

    /// <summary>Example sentence.</summary>
    Example,

    /// <summary>Related-content asset group.</summary>
    AssetBox,

    /// <summary>Title row inside a boxed dictionary note.</summary>
    BoxHeading,

    /// <summary>Boxed grammar, thesaurus, or collocation content.</summary>
    Box,

    /// <summary>Activator concept or section block.</summary>
    ActivatorSection,

    /// <summary>Activator concept navigation pane.</summary>
    ActivatorConcept,

    /// <summary>Image block.</summary>
    Image,
}

/// <summary>
/// Visual role for inline text.
/// </summary>
public enum DictionaryTextStyle
{
    /// <summary>Normal text.</summary>
    Normal,

    /// <summary>Headword text.</summary>
    Headword,

    /// <summary>Hyphenated entry title text.</summary>
    Hyphenation,

    /// <summary>Homograph number displayed after an entry title.</summary>
    HomonymNumber,

    /// <summary>Part of speech or label.</summary>
    Label,

    /// <summary>Part of speech in compact lists.</summary>
    PartOfSpeech,

    /// <summary>Definition text.</summary>
    Definition,

    /// <summary>Example text.</summary>
    Example,

    /// <summary>Bold text inside an example sentence.</summary>
    ExampleStrong,

    /// <summary>Bold text using the normal foreground color.</summary>
    Emphasis,

    /// <summary>Related-content asset title text.</summary>
    AssetTitle,

    /// <summary>Word-family part-of-speech heading text.</summary>
    WordFamilyPartOfSpeech,

    /// <summary>Bold lexical item.</summary>
    Strong,

    /// <summary>Word origin form.</summary>
    Origin,

    /// <summary>Muted supporting text.</summary>
    Muted,

    /// <summary>Badge text.</summary>
    Badge,

    /// <summary>Uppercase sense signpost tag.</summary>
    Signpost,

    /// <summary>Uppercase synonym or opposite marker tag.</summary>
    RelationTag,

    /// <summary>Frequency or academic-word-list badge.</summary>
    FrequencyTag,
}

/// <summary>
/// Link target kind.
/// </summary>
public enum DictionaryLinkTargetKind
{
    /// <summary>Internal dictionary path.</summary>
    DictionaryPath,

    /// <summary>Lookup query in the search box.</summary>
    Lookup,

    /// <summary>External URL.</summary>
    External,

    /// <summary>Full-size dictionary image.</summary>
    Image,
}
