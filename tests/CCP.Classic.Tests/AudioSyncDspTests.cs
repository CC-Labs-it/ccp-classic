using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioSyncDspTests
{
    [Fact]
    public void ActualFftBassSeparatesLowAndHighAudioWhileRmsStillMeasuresBoth()
    {
        var envelope = new RmsEnvelope(); var samples = TonePair(); var accumulator = new PcmRmsAccumulator(envelope, channels: 1);
        accumulator.Append(samples.AsSpan(0, 1234), .8); accumulator.Append(samples.AsSpan(1234), .8 + 1234d / 48000);
        envelope.Finish();
        Assert.Equal(0, envelope.Sample(.5)); Assert.Equal(default, envelope.SampleFeatures(.5));
        Assert.True(envelope.Sample(1) > .3); Assert.True(envelope.Sample(1.2) > .3);
        Assert.True(envelope.SampleFeatures(1).Bass > envelope.SampleFeatures(1.2).Bass * 20);
        // FFT features are anchored to their window start, so the frequency change appears in the windows spanning 1.05s.
        Assert.True(Enumerable.Range(0, 7).Max(index => envelope.SampleFeatures(1.01 + index * .01).Onset) > .1);
    }

    [Fact]
    public void WeightsSensitivitySmoothingAndMaximumEachChangeOutputButSilenceHasNoFloor()
    {
        var options = new AudioSyncDspOptions { Smoothing = 0, RmsWeight = 1, BassWeight = 0, OnsetWeight = 0 };
        var previous = 0d; Assert.Equal(.25, AudioSyncDsp.Combine(new(.25, .5, 1), options, ref previous), 8);
        options.Sensitivity = 2; Assert.Equal(.5, AudioSyncDsp.Combine(new(.25, 0, 0), options, ref previous), 8);
        options.Sensitivity = 1; options.Smoothing = .5; previous = 0;
        Assert.Equal(.125, AudioSyncDsp.Combine(new(.25, 0, 0), options, ref previous), 8);
        options.Smoothing = 0; options.MaximumIntensity = .5; Assert.Equal(.5, AudioSyncDsp.Combine(new(1, 0, 0), options, ref previous), 8);
        Assert.Equal(0, AudioSyncDsp.Combine(default, options, ref previous)); Assert.Equal(0, previous);
        options.RmsWeight = 0; options.BassWeight = 1; Assert.Equal(.5, AudioSyncDsp.Combine(new(0, .5, 0), options, ref previous), 8);
        options.BassWeight = 0; options.OnsetWeight = 1; Assert.Equal(.5, AudioSyncDsp.Combine(new(0, 0, .5), options, ref previous), 8);
    }

    [Fact]
    public async Task OffsetPauseAndExpiredSamplesUseSourceTimeAndClearSmoothingMemory()
    {
        var envelope = new RmsEnvelope(); new PcmRmsAccumulator(envelope, channels: 1).Append(TonePair(), 0);
        envelope.Finish();
        var clock = TimeProvider.System; var options = new AudioSyncDspOptions { Smoothing = 0, MinimumIntensity = 0, RmsWeight = 0, BassWeight = 1, OnsetWeight = 0 };
        var processor = new AudioSyncDsp(); var sample = new AudioPlaybackSample("clip", .2, .4, true, clock.GetTimestamp());
        var low = 0d;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(3));
        while (low == 0)
        { sample = sample with { ObservedAt = clock.GetTimestamp() }; low = processor.Sample([sample], _ => envelope, clock, options, out _); if (low == 0) await Task.Delay(10, stop.Token); }
        options.OffsetMilliseconds = 200;
        Assert.True(processor.Sample([sample], _ => envelope, clock, options, out _) < low / 10);
        Assert.Equal(0, processor.Sample([sample with { IsPlaying = false }], _ => envelope, clock, options, out _));
        Assert.Equal(0, processor.Sample([sample with { ObservedAt = clock.GetTimestamp() - clock.TimestampFrequency }], _ => envelope, clock, options, out _));
    }

    private static short[] TonePair() => Enumerable.Range(0, 24000).Select(index =>
        (short)(Math.Sin(Math.Tau * (index < 12000 ? 100 : 3000) * index / 48000) * 16384)).ToArray();
}
