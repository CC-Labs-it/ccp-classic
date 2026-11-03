using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioFeatureTrackTests
{
    private static AudioFeatureTrack.DspKey Key(AudioSyncDspOptions? options = null) => AudioFeatureTrack.DspKey.From(options ?? new() { Smoothing = 0 });

    [Fact]
    public void ChunkPeakAndMedianGateUseTheEntireChunkInsteadOfTheCurrentPrefix()
    {
        var frames = Enumerable.Range(0, 50).Select(index => new PcmAudioFeature(index / 100d, 1, 0, index < 25 ? .05f : 1, 0, 0, 0)).ToArray();
        var options = new AudioSyncDspOptions { Smoothing = 0, RmsWeight = 0, BassWeight = 0, OnsetWeight = 1, MinimumIntensity = 0 };
        var result = AudioFeatureTrack.Render(frames, Key(options));
        Assert.All(result.Take(25), value => Assert.Equal(0, value.Combined));
        Assert.All(result.Skip(25), value => Assert.Equal(1, value.Combined));
    }

    [Fact]
    public void OriginalHighAndBassTransientsOverrideShapingForExactlySixAndEightHops()
    {
        var frames = Enumerable.Range(0, 40).Select(index => new PcmAudioFeature(index / 100d, 1, 1, 0,
            index == 20 ? 20 : 0, index == 30 ? 20 : 0, 1)).ToArray();
        var options = new AudioSyncDspOptions { Smoothing = .9, MaximumIntensity = .5 };
        var result = AudioFeatureTrack.Render(frames, Key(options));
        Assert.All(result.Skip(20).Take(6), value => Assert.Equal(1, value.Combined));
        Assert.True(result[26].Combined < 1);
        Assert.All(result.Skip(30).Take(8), value => Assert.Equal(.95, value.Combined));
        Assert.True(result[38].Combined < .95);
    }

    [Fact]
    public void BassAndHighBandsAreSeparateAndTheCombinedQuietGateSilencesBoth()
    {
        var frames = Enumerable.Range(0, 50).Select(index => new PcmAudioFeature(index / 100d, 1, index < 25 ? 1 : 0,
            index == 10 ? .01f : 1, 0, 0, index < 25 ? 0 : 1)).ToArray();
        var options = new AudioSyncDspOptions { Smoothing = 0, RmsWeight = 0, BassWeight = 0, OnsetWeight = 1, MinimumIntensity = 0 };
        var result = AudioFeatureTrack.Render(frames, Key(options));
        Assert.Equal(new(0, 0, 0), result[10]);
        Assert.Equal(new(1, 1, 0), result[20]); Assert.Equal(new(1, 0, 1), result[30]);
    }

    [Fact]
    public async Task TimestampedChunksRemainSilentUntilReadyAndNewOptionsRebuildOnlyTheirCurve()
    {
        var track = new AudioFeatureTrack(); var options = new AudioSyncDspOptions { Smoothing = 0, MinimumIntensity = 0, RmsWeight = 0, BassWeight = 0, OnsetWeight = 1 };
        track.Append(new(0, 1, 1, .5f, 0, 0, 0)); track.Append(new(.1, 1, 1, 1, 0, 0, 0));
        Assert.Equal(default, track.Sample(0, options)); track.Seal();
        await Until(() => track.Sample(0, options).Combined == .5);
        options.Sensitivity = 2; await Until(() => Math.Abs(track.Sample(0, options).Combined - Math.Sqrt(.5)) < 1e-8);
        Assert.Equal(default, track.Sample(10, options));
    }
    private static async Task Until(Func<bool> ready)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(3));
        while (!ready()) await Task.Delay(10, stop.Token);
    }
}
