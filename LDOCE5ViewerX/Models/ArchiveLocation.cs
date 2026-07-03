namespace LDOCE5ViewerX.Models;

/// <summary>
/// Identifies one decompressed slice inside a compressed LDOCE archive block.
/// </summary>
/// <param name="CompressedOffset">Byte offset of the compressed block in <c>CONTENT.tda</c>.</param>
/// <param name="CompressedSize">Compressed block length in bytes.</param>
/// <param name="OriginalOffset">Byte offset of the item inside the decompressed block.</param>
/// <param name="OriginalSize">Item length inside the decompressed block.</param>
public sealed record ArchiveLocation(
    int CompressedOffset,
    int CompressedSize,
    int OriginalOffset,
    int OriginalSize);
