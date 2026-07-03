namespace LDOCE5ViewerX.Models;

/// <summary>
/// One styled text segment in a search result label.
/// </summary>
/// <param name="Text">Visible text for the segment.</param>
/// <param name="Style">Visual role for search result label text.</param>
public sealed record SearchResultLabelRun(string Text, SearchResultLabelStyle Style);

/// <summary>
/// Visual role for search result label text.
/// </summary>
public enum SearchResultLabelStyle
{
    /// <summary>Default muted result-list text.</summary>
    Normal,

    /// <summary>Headword group text.</summary>
    Headword,

    /// <summary>Bold headword text.</summary>
    HeadwordStrong,

    /// <summary>Phrasal verb headword text.</summary>
    PhrasalVerb,

    /// <summary>Part-of-speech text.</summary>
    PartOfSpeech,

    /// <summary>Superscript homograph number.</summary>
    Superscript,

    /// <summary>Activator concept text.</summary>
    ActivatorConcept,

    /// <summary>Activator exponent text.</summary>
    ActivatorExponent,

    /// <summary>Collocation head text.</summary>
    CollocationText,

    /// <summary>Lexunit or phrase head text.</summary>
    PhraseText,
}
