using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class HapticBridgeTests
{
    [Fact]
    public void MeterUsesSourceTimeAndEffectiveGainWithoutAmplifyingOverlappingVoices()
    {
        var signal = new RmsEnvelope(); new PcmRmsAccumulator(signal, 100, 2).Append(Enumerable.Repeat((short)16384, 10).ToArray(), 12);
        var clock = new Clock(); var voice = new AudioPlaybackSample("local.wav", 12.025, .4, true, 0);
        Assert.InRange(DesktopEffectsService.SampleAudioLevel([voice, voice], _ => signal, clock, out var failed), .1999, .2001);
        Assert.False(failed);
        Assert.Equal(0, DesktopEffectsService.SampleAudioLevel([voice with { SourceSeconds = .025 }], _ => signal, clock, out _));
        Assert.Equal(0, DesktopEffectsService.SampleAudioLevel([voice with { Gain = 0 }], _ => signal, clock, out _));
    }

    [Fact]
    public void PausedStaleAndInvalidObservationsNeverRequestDecodingOrSustainOutput()
    {
        var clock = new Clock(); var signal = new RmsEnvelope(); signal.Publish(0, .5, .05);
        var voice = new AudioPlaybackSample("local.wav", .025, 1, true, 0); var requests = 0;
        RmsEnvelope Get(string _) { requests++; return signal; }
        Assert.Equal(0, DesktopEffectsService.SampleAudioLevel([voice with { IsPlaying = false }, voice with { Gain = double.NaN },
            voice with { SourceSeconds = double.NaN }], Get, clock, out _));
        Assert.Equal(0, requests);
        clock.Now = 299; Assert.Equal(.5, DesktopEffectsService.SampleAudioLevel([voice], Get, clock, out _));
        clock.Now = 300; Assert.Equal(0, DesktopEffectsService.SampleAudioLevel([voice], Get, clock, out _));
        Assert.Equal(1, requests);
        Assert.Equal(0, DesktopEffectsService.SampleAudioLevel([voice with { ObservedAt = 301 }], Get, clock, out _));
    }

    [Fact]
    public void UnavailableAnalysisFailsZeroAndTheNumberOfSourcesIsBounded()
    {
        var clock = new Clock(); var voice = new AudioPlaybackSample("local.wav", 0, 1, true, 0); var calls = 0;
        var level = DesktopEffectsService.SampleAudioLevel(Enumerable.Repeat(voice, 1000), _ => { calls++; return RmsEnvelope.Unavailable; }, clock, out var failed);
        Assert.True(failed); Assert.Equal(0, level); Assert.Equal(64, calls);
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
}
