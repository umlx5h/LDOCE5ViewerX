using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

using LDOCE5ViewerX.Models;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Loads and renders selected dictionary entries using generated index files and local LDOCE data.
/// </summary>
public sealed class DictionaryContentService : IDisposable
{
    private readonly FilemapReader _filemapReader;
    private readonly LdoceArchiveReader _archiveReader;
    private readonly Func<IEnumerable<WebSearchSite>> _webSearchSitesProvider;
    private readonly Func<WebSearchAssetBoxMode> _webSearchAssetBoxModeProvider;

    /// <summary>
    /// Creates the content service from the index directory and dictionary data directory.
    /// </summary>
    /// <param name="indexPaths">Filesystem paths used by generated indexes.</param>
    /// <param name="dataDirectory">Local <c>ldoce5.data</c> directory.</param>
    /// <param name="webSearchSitesProvider">Provides the configured web search engines.</param>
    /// <param name="webSearchAssetBoxModeProvider">Provides the configured AssetBox web search display mode.</param>
    public DictionaryContentService(
        IndexPaths indexPaths,
        string dataDirectory,
        Func<IEnumerable<WebSearchSite>>? webSearchSitesProvider = null,
        Func<WebSearchAssetBoxMode>? webSearchAssetBoxModeProvider = null)
    {
        _filemapReader = new FilemapReader(indexPaths.FilemapPath);
        _archiveReader = new LdoceArchiveReader(dataDirectory);
        _webSearchSitesProvider = webSearchSitesProvider ?? WebSearchLinks.CreateDefaultSites;
        _webSearchAssetBoxModeProvider = webSearchAssetBoxModeProvider ?? (() => WebSearchAssetBoxMode.NounEntriesOnly);
    }

    /// <summary>
    /// Loads rich content for a selected dictionary path.
    /// </summary>
    /// <param name="path">Dictionary path, such as <c>/fs/example</c> or <c>/activator/id/section</c>.</param>
    /// <returns>Rich dictionary content page.</returns>
    public DictionaryContentPage LoadContent(string path)
    {
        string? anchor = GetFragment(path);
        string cleanPath = StripFragment(path);
        string[] parts = cleanPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidDataException("The dictionary path is invalid.");
        }

        (string title, DictionaryDocument document) = parts[0] switch
        {
            "fs" => RichContentRenderer.RenderEntry(
                LoadArchiveItem("fs", parts[1]),
                _webSearchSitesProvider(),
                _webSearchAssetBoxModeProvider()),
            "activator" when parts.Length >= 3 => LoadActivatorContent(parts[1], parts[2]),
            "collocations" => RichContentRenderer.RenderCollocations(LoadArchiveItem("collocations", parts[1])),
            "examples" => RichContentRenderer.RenderExamples(LoadArchiveItem("examples", parts[1])),
            "word_families" => RichContentRenderer.RenderWordFamilies(LoadArchiveItem("word_families", parts[1])),
            "etymologies" => RichContentRenderer.RenderEtymologies(LoadArchiveItem("etymologies", parts[1])),
            "phrases" => RichContentRenderer.RenderPhrases(LoadArchiveItem("phrases", parts[1])),
            "thesaurus" => RichContentRenderer.RenderThesaurus(LoadArchiveItems("thesaurus", parts[1])),
            "word_sets" => RichContentRenderer.RenderWordSets(LoadArchiveItems("word_sets", parts[1])),
            _ => ($"Content unavailable: {parts[0]}", DictionaryDocument.FromPlainText($"Rich display for '{parts[0]}' entries is not implemented yet.")),
        };

        return new DictionaryContentPage(title, cleanPath, anchor, document);
    }

    /// <summary>
    /// Loads raw bytes for a dictionary image or audio resource.
    /// </summary>
    /// <param name="resource">Resource archive and item name.</param>
    /// <returns>Raw resource bytes.</returns>
    public byte[] LoadResource(DictionaryResourceRef resource)
    {
        return LoadArchiveItem(resource.Archive, resource.Name);
    }

#if DEBUG
    /// <summary>
    /// Loads the XML backing a dictionary content path for temporary debugging tools.
    /// </summary>
    /// <param name="path">Dictionary path, such as <c>/fs/example</c> or <c>/activator/id/section</c>.</param>
    /// <returns>Human-readable XML text for the selected content.</returns>
    public string LoadRawXml(string path)
    {
        string[] parts = StripFragment(path).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidDataException("The dictionary path is invalid.");
        }

        return parts[0] switch
        {
            "fs" => LoadFormattedXmlItem("fs", parts[1]),
            "activator" when parts.Length >= 3 => LoadActivatorRawXml(parts[1], parts[2]),
            "collocations" => LoadFormattedXmlItem("collocations", parts[1]),
            "examples" => LoadFormattedXmlItem("examples", parts[1]),
            "word_families" => LoadFormattedXmlItem("word_families", parts[1]),
            "etymologies" => LoadFormattedXmlItem("etymologies", parts[1]),
            "phrases" => LoadFormattedXmlItem("phrases", parts[1]),
            "thesaurus" => LoadRawXmlItems("thesaurus", parts[1]),
            "word_sets" => LoadRawXmlItems("word_sets", parts[1]),
            _ => throw new InvalidDataException($"Raw XML is not available for '{parts[0]}' content."),
        };
    }
#endif

    /// <summary>
    /// Loads plain text for a selected incremental search result path.
    /// </summary>
    /// <param name="path">Dictionary path, such as <c>/fs/example</c> or <c>/activator/id/section</c>.</param>
    /// <returns>Plain text content for the selected item.</returns>
    public string LoadPlainText(string path)
    {
        string[] parts = StripFragment(path).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidDataException("The dictionary path is invalid.");
        }

        return parts[0] switch
        {
            "fs" => LoadSingleXmlItem("fs", parts[1]),
            "activator" when parts.Length >= 3 => LoadActivator(parts[1], parts[2]),
            _ => $"Plain-text display for '{parts[0]}' entries is not implemented yet.",
        };
    }

    /// <summary>
    /// Removes an in-entry anchor before resolving the archive item through the file map.
    /// </summary>
    private static string StripFragment(string path)
    {
        int fragmentIndex = path.IndexOf('#', StringComparison.Ordinal);
        return fragmentIndex < 0 ? path : path[..fragmentIndex];
    }

    /// <summary>
    /// Gets the in-entry anchor from a dictionary path.
    /// </summary>
    private static string? GetFragment(string path)
    {
        int fragmentIndex = path.IndexOf('#', StringComparison.Ordinal);
        return fragmentIndex < 0 || fragmentIndex == path.Length - 1 ? null : path[(fragmentIndex + 1)..];
    }

    /// <summary>
    /// Loads one XML archive item and renders it as plain text.
    /// </summary>
    private string LoadSingleXmlItem(string archive, string name)
    {
        byte[] data = LoadArchiveItem(archive, name);
        return PlainTextContentRenderer.RenderXml(data);
    }

#if DEBUG
    /// <summary>
    /// Loads one XML archive item as indented UTF-8 text.
    /// </summary>
    private string LoadFormattedXmlItem(string archive, string name)
    {
        string xml = Encoding.UTF8.GetString(LoadArchiveItem(archive, name));
        return XDocument.Parse(xml, LoadOptions.None).ToString(SaveOptions.None);
    }

    /// <summary>
    /// Loads multiple underscore-separated XML archive items as indented UTF-8 text.
    /// </summary>
    private string LoadRawXmlItems(string archive, string names)
    {
        string[] items = names.Split('_', StringSplitOptions.RemoveEmptyEntries);
        string[] xml = new string[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            xml[i] = LoadFormattedXmlItem(archive, items[i]);
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", xml);
    }
#endif

    /// <summary>
    /// Loads the two XML files that make up an activator page and joins their plain text.
    /// </summary>
    private string LoadActivator(string conceptId, string sectionId)
    {
        string concept = LoadSingleXmlItem("activator_concept", conceptId);
        string section = LoadSingleXmlItem("activator_section", sectionId);
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", concept, section);
    }

#if DEBUG
    /// <summary>
    /// Loads the two XML files that make up an activator page as indented UTF-8 text.
    /// </summary>
    private string LoadActivatorRawXml(string conceptId, string sectionId)
    {
        string concept = LoadFormattedXmlItem("activator_concept", conceptId);
        string section = LoadFormattedXmlItem("activator_section", sectionId);
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", concept, section);
    }
#endif

    /// <summary>
    /// Loads an activator page as rich content.
    /// </summary>
    private (string Title, DictionaryDocument Document) LoadActivatorContent(string conceptId, string sectionId)
    {
        byte[] concept = LoadArchiveItem("activator_concept", conceptId);
        byte[] section = LoadArchiveItem("activator_section", sectionId);
        return RichContentRenderer.RenderActivator(concept, section, sectionId);
    }

    /// <summary>
    /// Loads multiple underscore-separated archive items.
    /// </summary>
    private byte[][] LoadArchiveItems(string archive, string names)
    {
        string[] items = names.Split('_', StringSplitOptions.RemoveEmptyEntries);
        byte[][] data = new byte[items.Length][];
        for (int i = 0; i < items.Length; i++)
        {
            data[i] = LoadArchiveItem(archive, items[i]);
        }

        return data;
    }

    /// <summary>
    /// Resolves an archive item through the file map and reads its raw bytes.
    /// </summary>
    private byte[] LoadArchiveItem(string archive, string name)
    {
        Models.ArchiveLocation? location = _filemapReader.Lookup(archive, name);
        if (location is null)
        {
            throw new FileNotFoundException("The selected dictionary item was not found in the file map.", name);
        }

        return _archiveReader.Read(archive, location);
    }

    /// <summary>
    /// Releases generated-index readers owned by the content service.
    /// </summary>
    public void Dispose()
    {
        _filemapReader.Dispose();
    }
}
