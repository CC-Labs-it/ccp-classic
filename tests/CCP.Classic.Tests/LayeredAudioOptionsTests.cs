using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LayeredAudioOptionsTests
{
    [Fact]
    public void SourceDefaultsAndBoundedDeepCopiesPreserveTrackIdentity()
    {
        var options = new LayeredAudioOptions(); Assert.False(options.Enabled); Assert.False(options.AudioOnlySession);
        Assert.Equal(70, options.MasterVolume); var track = new AudioLayerTrack(); Assert.Equal(70, track.Volume); Assert.True(track.Enabled);
        options.Tracks.Add(track); var copy = options.Copy(); copy.Tracks[0].Volume = 14;
        Assert.Equal(track.Id, copy.Tracks[0].Id); Assert.Equal(70, track.Volume);
        options.MasterVolume = 1000; options.Tracks = Enumerable.Range(0, 20).Select(_ => new AudioLayerTrack
            { Id = Guid.Empty, Path = "https://example.test/audio.wav", Volume = -3 }).ToList();
        options.Normalize(); Assert.Equal(100, options.MasterVolume); Assert.Equal(8, options.Tracks.Count);
        Assert.Equal(8, options.Tracks.Select(item => item.Id).Distinct().Count());
        Assert.All(options.Tracks, item => { Assert.Empty(item.Path); Assert.Equal(0, item.Volume); });
    }
    [Fact]
    public void ResetRemovesLayerConfigurationAndOldSettingsAcquireSafeDefaults()
    {
        var settings = new ClassicSettings { LayeredAudio = new() { Enabled = true, AudioOnlySession = true, MasterVolume = 20 } };
        settings.ResetToDefaults(); Assert.False(settings.LayeredAudio.Enabled); Assert.False(settings.LayeredAudio.AudioOnlySession);
        settings.LayeredAudio = null!; settings.Normalize(); Assert.Equal(70, settings.LayeredAudio.MasterVolume);
    }
}
