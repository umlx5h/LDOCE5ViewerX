using AwesomeAssertions;

namespace LDOCE5ViewerX.Services;

public sealed class AudioPlayerFactoryTests
{
    [Fact]
    public void CreateDefault_returns_miniaudio_player()
    {
        using IAudioPlayer player = AudioPlayerFactory.CreateDefault();

        player.Should().BeOfType<MiniAudioAudioPlayer>();
    }
}
