using System;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Plays short dictionary audio resources.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>
    /// Plays audio bytes.
    /// </summary>
    /// <param name="data">Encoded audio bytes.</param>
    /// <param name="mediaType">Audio media type.</param>
    /// <param name="volume">Linear playback volume, where <c>1.0</c> is the backend default.</param>
    void Play(byte[] data, string mediaType, double volume);
}
