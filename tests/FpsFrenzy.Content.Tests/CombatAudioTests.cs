using FpsFrenzy.Kni.Audio;
using Microsoft.Xna.Framework.Media;

namespace FpsFrenzy.Content.Tests;

public sealed class CombatAudioTests
{
    [Fact]
    public void VictoryStingerMustPlayBeforeResultsMusicBegins()
    {
        Assert.Equal("victory", CombatAudio.GetMusicAssetName(AudioMusicState.Victory));
        Assert.Equal("transmission", CombatAudio.GetMusicAssetName(AudioMusicState.Results));

        Assert.Null(CombatAudio.ResolveVictoryFollowUp(
            AudioMusicState.Victory,
            stingerActive: true,
            observedPlaying: false,
            MediaState.Stopped));
        Assert.Null(CombatAudio.ResolveVictoryFollowUp(
            AudioMusicState.Victory,
            stingerActive: true,
            observedPlaying: true,
            MediaState.Playing));

        Assert.Equal(
            AudioMusicState.Results,
            CombatAudio.ResolveVictoryFollowUp(
                AudioMusicState.Victory,
                stingerActive: true,
                observedPlaying: true,
                MediaState.Stopped));
        Assert.Null(CombatAudio.ResolveVictoryFollowUp(
            AudioMusicState.Combat,
            stingerActive: true,
            observedPlaying: true,
            MediaState.Stopped));
    }
}
