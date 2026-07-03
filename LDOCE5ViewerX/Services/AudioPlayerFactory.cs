namespace LDOCE5ViewerX.Services;

/// <summary>
/// Creates dictionary audio players.
/// </summary>
public static class AudioPlayerFactory
{
    /// <summary>
    /// Creates the default audio player.
    /// </summary>
    /// <returns>The MiniAudio-backed audio player.</returns>
    public static IAudioPlayer CreateDefault()
    {
        return new MiniAudioAudioPlayer();
    }
}
