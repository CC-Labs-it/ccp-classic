using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioEnvelopeTests
{
    [Fact]
    public async Task MissingLocalFileNeverPublishesAnEnvelope()
    {
        using var cache = new AudioEnvelopeCache(); var envelope = cache.Get(PathFor(0));
        await cache.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(envelope.Failed); Assert.False(envelope.IsReady); Assert.Equal(0, envelope.Sample(0));
    }

    [Fact]
    public void StereoEnergyDoesNotCancelOppositeChannelsAndUsesRealSourcePositions()
    {
        var envelope = new RmsEnvelope(); var meter = new PcmRmsAccumulator(envelope, rate: 100, channels: 2);
        meter.Append(Enumerable.Range(0, 5).SelectMany(_ => new short[] { 16384, -16384 }).ToArray(), .2);
        Assert.True(envelope.IsReady); Assert.InRange(envelope.Sample(.225), .49999, .50001);
        Assert.Equal(0, envelope.Sample(.025)); Assert.Equal(0, envelope.Sample(.15));
        Assert.Equal(0, envelope.Sample(.25)); Assert.Equal(0, envelope.Sample(-1)); Assert.Equal(0, envelope.Sample(double.NaN));
    }

    [Fact]
    public void RmsIsNeitherPeakNorVolumeAndCallbackChunkBoundariesDoNotChangeIt()
    {
        var impulse = new RmsEnvelope(); var meter = new PcmRmsAccumulator(impulse, rate: 100, channels: 2);
        meter.Append([32767, 32767, 0, 0, 0, 0, 0, 0, 0, 0], 0);
        Assert.InRange(impulse.Sample(.025), .4471, .4473);
        var split = new RmsEnvelope(); var pieces = new PcmRmsAccumulator(split, rate: 100, channels: 2);
        pieces.Append([16384, 16384, 16384, 16384], 0);
        pieces.Append([16384, 16384, 16384, 16384, 16384, 16384], .02);
        Assert.InRange(split.Sample(.025), .49999, .50001);
        var silence = new RmsEnvelope(); new PcmRmsAccumulator(silence, rate: 100, channels: 2).Append(new short[10], 0);
        Assert.True(silence.IsReady); Assert.Equal(0, silence.Sample(.025));
    }

    [Fact]
    public void TimestampGapsAndDurationCapsNeverInventSustainedEnergy()
    {
        var envelope = new RmsEnvelope(); var meter = new PcmRmsAccumulator(envelope, rate: 100, channels: 2);
        meter.Append(Enumerable.Repeat((short)16384, 10).ToArray(), 0);
        meter.Append(Enumerable.Repeat((short)16384, 10).ToArray(), .2);
        Assert.Equal(0, envelope.Sample(.125)); Assert.InRange(envelope.Sample(.225), .49999, .50001);
        meter.Append([16384, 16384], 7200); Assert.True(meter.AtLimit);
        Assert.Equal(0, envelope.Sample(7200)); Assert.InRange(envelope.DurationSeconds, .24, .26);
    }

    [Fact]
    public async Task CompletedFileLruStaysAtSixteenAndEvictedSnapshotsBecomeSilent()
    {
        using var cache = new AudioEnvelopeCache((_, meter, _) => meter.Append(Enumerable.Repeat((short)16384, 4800).ToArray(), 0));
        var envelopes = new List<RmsEnvelope>();
        for (var index = 0; index < 16; index++)
        { envelopes.Add(cache.Get(PathFor(index))); await cache.Completion.WaitAsync(TestContext.Current.CancellationToken); }
        Assert.Same(envelopes[0], cache.Get(PathFor(0)));
        var next = cache.Get(PathFor(16)); await cache.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(16, cache.Count); Assert.True(envelopes[0].IsReady);
        Assert.True(envelopes[1].Failed); Assert.Equal(0, envelopes[1].Sample(.025));
        Assert.InRange(next.Sample(.025), .49999, .50001); Assert.True(next.IsComplete);
        cache.Clear(); Assert.Equal(0, next.Sample(.025)); Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task QueueBurstsUseOneDecoderAndDisposalHidesEvenAResultThatArrivesLate()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var peak = 0;
        var token = TestContext.Current.CancellationToken;
        using var cache = new AudioEnvelopeCache((_, meter, _) =>
        {
            var count = Interlocked.Increment(ref active); peak = Math.Max(peak, count); entered.TrySetResult();
            try { release.Wait(token); meter.Append(Enumerable.Repeat((short)16384, 4800).ToArray(), 0); }
            finally { Interlocked.Decrement(ref active); }
        });
        var first = cache.Get(PathFor(0)); await entered.Task.WaitAsync(token);
        for (var index = 1; index < 100; index++) cache.Get(PathFor(index));
        Assert.InRange(cache.Count, 1, 16); Assert.InRange(cache.PendingCount, 1, 16);
        cache.Dispose(); release.Set(); await cache.Completion.WaitAsync(token);
        Assert.Equal(1, peak); Assert.True(first.Failed); Assert.Equal(0, first.Sample(.025));
        Assert.True(cache.Get(PathFor(100)).Failed);
    }

    [Fact]
    public async Task FailedAnalysisAndInvalidPathsRemainSilentWithoutNativeRetries()
    {
        var calls = 0;
        using var cache = new AudioEnvelopeCache((_, _, _) => { calls++; throw new IOException("Failure"); });
        Assert.True(cache.Get("https://example.invalid/audio.wav").Failed); Assert.Equal(0, calls);
        var envelope = cache.Get(PathFor(0)); await cache.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(envelope.Failed); Assert.True(envelope.IsComplete); Assert.Equal(0, envelope.Sample(0));
        Assert.Same(envelope, cache.Get(PathFor(0))); Assert.Equal(1, calls);
    }

    private static string PathFor(int index) => Path.Combine(Path.GetTempPath(), "ccp-rms-test-" + index + ".wav");
}
