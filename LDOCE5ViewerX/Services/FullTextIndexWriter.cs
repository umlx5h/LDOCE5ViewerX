using System;
using System.IO;

using LDOCE5ViewerX.Models;

using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Writes LeanCorpus full-text indexes for extracted dictionary items.
/// </summary>
public sealed class FullTextIndexWriter : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly IndexWriter _writer;

    /// <summary>
    /// Creates a LeanCorpus writer and recreates the destination index.
    /// </summary>
    /// <param name="indexDirectory">Destination LeanCorpus index directory.</param>
    public FullTextIndexWriter(string indexDirectory)
    {
        if (System.IO.Directory.Exists(indexDirectory))
        {
            System.IO.Directory.Delete(indexDirectory, recursive: true);
        }

        System.IO.Directory.CreateDirectory(indexDirectory);
        _directory = new MMapDirectory(indexDirectory);
        IndexWriterConfig config = new()
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
        };
        _writer = new IndexWriter(_directory, config);
    }

    /// <summary>
    /// Adds one extracted item to the LeanCorpus index.
    /// </summary>
    /// <param name="item">Extracted searchable item.</param>
    public void AddItem(SearchIndexItem item)
    {
        LeanDocument document = new();
        document.Add(new TextField("content", TextNormalizer.NormalizeFullText(item.Content), stored: false));
        document.Add(new TextField("asfilter", item.AdvancedSearchFilter, stored: false));
        document.Add(new StringField("type", item.TypeCode, stored: true));
        document.Add(new StoredField("path", item.Path));
        document.Add(new StoredField("sortkey", TextNormalizer.NormalizeIndexKey(item.SortKey)));
        document.Add(new StoredField("contentraw", item.Content));
        document.Add(new StoredField("label", item.Label));
        document.Add(new StoredField("priority", item.Priority));
        _writer.AddDocument(document);
    }

    /// <summary>
    /// Commits pending LeanCorpus documents.
    /// </summary>
    public void Commit()
    {
        _writer.Commit();
    }

    /// <summary>
    /// Disposes the underlying LeanCorpus writer.
    /// </summary>
    public void Dispose()
    {
        _writer.Dispose();
        _directory.Dispose();
    }
}
