using System.Collections.Generic;

namespace LDOCE5ViewerX.Models;

/// <summary>
/// Describes one file entry discovered in an IDM archive catalog.
/// </summary>
/// <param name="Directories">Logical directory path components from <c>dirs.skn</c>.</param>
/// <param name="Name">Original file name from <c>files.skn</c>.</param>
/// <param name="Location">Compressed archive block location.</param>
public sealed record ArchiveFileEntry(
    IReadOnlyList<string> Directories,
    string Name,
    ArchiveLocation Location);
