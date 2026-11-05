using System.Security.Cryptography;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class RecordedBubblePopTests
{
    [Fact]
    public void BundledPopsMatchAllThreeOriginalPublicRecordings()
    {
        var expected = new Dictionary<string, (int Bytes, string Hash)>
        {
            ["Pop.mp3"] = (5065, "f6ed355d41899fe7af6b3330bd2e5156d2c13244253523b6ecdc6a61db8a596d"),
            ["Pop2.mp3"] = (3811, "f102b4354a0ac9f68dcad314ff0b6cd4b309cd12aceedde75cbe03fc2425d0be"),
            ["Pop3.mp3"] = (3811, "54e87d407679cd7ce7f29b967f502860e7e39ba5bca4ef789b5e6702204b6493")
        };
        var sounds = new RecordedBubblePops(); Assert.Equal(3, sounds.Clips.Count);
        foreach (var clip in sounds.Clips)
        {
            var source = expected[Path.GetFileName(clip)]; var bytes = File.ReadAllBytes(clip);
            Assert.Equal(source.Bytes, bytes.Length);
            Assert.Equal(source.Hash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Equal("mp3", LocalAudioInput.DetectDemux(bytes));
        }
    }

    [Fact]
    public void RecordedSelectionUsesTheExistingGainMuteAndStopGate()
    {
        var sounds = new RecordedBubblePops(seed: 4); var playback = new Playback();
        var gate = new AudioPlaybackGate(playback);
        Assert.False(sounds.Play(gate, true)); Assert.Empty(playback.Played);
        gate.Update(true, .4); Assert.False(sounds.Play(gate, false));
        for (var index = 0; index < 64; index++) Assert.True(sounds.Play(gate, true));
        Assert.Equal(3, playback.Played.Select(item => item.Path).Distinct().Count());
        Assert.All(playback.Played, item => { Assert.Contains(item.Path, sounds.Clips); Assert.Equal(.7, item.Gain); });
        Assert.Equal(.4, playback.Master);
        gate.Update(false, 0); Assert.False(sounds.Play(gate, true)); Assert.Equal(64, playback.Played.Count);
        gate.Update(true, 1); gate.Stop(); Assert.False(sounds.Play(gate, true)); Assert.Equal(64, playback.Played.Count);
        Assert.False(new RecordedBubblePops(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Play(gate, true));
    }

    private sealed class Playback : IAudioPlayback
    {
        public List<(string Path, double Gain)> Played { get; } = [];
        public double Master;
        public bool Play(string localPath, double gain) { Played.Add((localPath, gain)); return true; }
        public void SetMasterGain(double gain) => Master = gain;
        public void StopAll() { }
        public void Dispose() { }
    }
}
