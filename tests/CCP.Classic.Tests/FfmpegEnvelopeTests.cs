using System.Diagnostics;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class FfmpegEnvelopeTests
{
    public static bool NativeRequested => Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_PROBE") == "1";

    [Fact]
    public void DecoderArgumentsPreserveLeadGapsAndKeepInputsOutputsAndThreadsBounded()
    {
        var path = Path.Combine(Path.GetTempPath(), "local & $ media.wav");
        var info = FfmpegEnvelopeDecoder.StartInfo(Path.Combine(Path.GetTempPath(), "ffmpeg.exe"), path, "wav");
        var arguments = info.ArgumentList.ToArray();
        Assert.False(info.UseShellExecute); Assert.True(info.CreateNoWindow); Assert.True(info.RedirectStandardOutput); Assert.True(info.RedirectStandardError);
        Assert.Contains(path, arguments); Assert.Contains("-copyts", arguments); Assert.DoesNotContain("-start_at_zero", arguments);
        Assert.Equal("file,pipe", arguments[Array.IndexOf(arguments, "-protocol_whitelist") + 1]);
        Assert.Equal("67108864", arguments[Array.IndexOf(arguments, "-max_alloc") + 1]);
        Assert.Equal("7200", arguments[Array.IndexOf(arguments, "-t") + 1]);
        Assert.Equal("aresample=48000:async=1:first_pts=0", arguments[Array.IndexOf(arguments, "-af") + 1]);
        Assert.Equal("pipe:1", arguments[^1]); Assert.Throws<ArgumentException>(() => FfmpegEnvelopeDecoder.StartInfo(info.FileName, path, "concat"));
    }
    [Fact]
    public void OnlyActualAllowlistedContainerHeadersChooseDemuxers()
    {
        Assert.Equal("wav", FfmpegEnvelopeDecoder.Demux("RIFF0000WAVE"u8, ".wav"));
        Assert.Equal("matroska", FfmpegEnvelopeDecoder.Demux([0x1a, 0x45, 0xdf, 0xa3], ".mkv"));
        Assert.Equal("mov", FfmpegEnvelopeDecoder.Demux("0000ftyp0000"u8, ".mp4"));
        Assert.Null(FfmpegEnvelopeDecoder.Demux("#EXTM3U\n"u8, ".mp3"));
        Assert.Null(FfmpegEnvelopeDecoder.Demux("[playlist]"u8, ".mp4"));
    }
    [Fact]
    public async Task OddPipeReadsKeepStereoFramesAndTheirMediaPositionsIntact()
    {
        var values = Enumerable.Repeat(new byte[] { 0, 64, 0, 192 }, 4800).SelectMany(frame => frame).ToArray();
        var envelope = new RmsEnvelope(); using var stream = new Pieces(values);
        await FfmpegEnvelopeDecoder.ReadPcm(stream, new(envelope), TestContext.Current.CancellationToken);
        Assert.InRange(envelope.DurationSeconds, .09999, .10001);
        Assert.InRange(envelope.Sample(.025), .49999, .50001); Assert.InRange(envelope.Sample(.075), .49999, .50001);
    }
    [Fact]
    public async Task TruncatedOutputAndExcessiveDurationCannotBecomeSuccessfulAnalysis()
    {
        await Assert.ThrowsAsync<IOException>(() => FfmpegEnvelopeDecoder.ReadPcm(new MemoryStream([1, 2, 3]), new(new()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => FfmpegEnvelopeDecoder.ReadPcm(new MemoryStream(new byte[8]), new(new()), TestContext.Current.CancellationToken, maximumFrames: 1));
    }
    [Fact]
    public async Task CancellationBeforeReadAndBeforePublicationDoesNotPublishLateEnergy()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.Cancel();
        using var unopened = new Pieces([0, 64, 0, 192]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FfmpegEnvelopeDecoder.ReadPcm(unopened, new(new()), stop.Token));
        Assert.Equal(0, unopened.Reads);
        using var during = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); var envelope = new RmsEnvelope();
        using var stream = new Pieces([0, 64, 0, 192], during.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FfmpegEnvelopeDecoder.ReadPcm(stream, new(envelope), during.Token));
        Assert.False(envelope.IsReady);
    }
    [Fact]
    public async Task InvalidLocalMediaFailsSilentlyThroughTheCache()
    {
        using var cache = new AudioEnvelopeCache(new FfmpegEnvelopeDecoder().Decode);
        var snapshot = cache.Get(Path.Combine(Path.GetTempPath(), "unavailable-analysis.wav"));
        await cache.Completion.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(snapshot.Failed); Assert.False(snapshot.IsReady); Assert.Equal(0, snapshot.Sample(1));
        Assert.DoesNotContain(Path.GetTempPath(), snapshot.FailureReason ?? "");
    }

    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 with the existing helper and generated fixtures.", SkipUnless = nameof(NativeRequested))]
    public async Task ExistingHelperMetersWaveDelayedVideoAndCommonLeadGapsWithoutSound()
    {
        var executable = Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_TEST_PATH")!;
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_FIXTURES")!;
        Exception? failure = null;
        using var cache = new AudioEnvelopeCache(new FfmpegEnvelopeDecoder(executable, failed: error => failure = error).Decode);
        foreach (var (name, full, half) in new[] { ("known.wav", .325, .925), ("delayed.mkv", .725, 1.325),
                     ("common-gap.mkv", 1.025, 1.725), ("common-gap.mov", 1.025, 1.725) })
        {
            var envelope = cache.Get(Path.Combine(folder, name)); await cache.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(envelope.Failed, failure?.ToString() ?? envelope.FailureReason); Assert.True(envelope.IsComplete);
            Assert.Equal(0, envelope.Sample(.025)); Assert.InRange(envelope.Sample(full), .49, .51); Assert.InRange(envelope.Sample(half), .24, .26);
            if (name.StartsWith("common-gap", StringComparison.Ordinal)) Assert.Equal(0, envelope.Sample(.725));
        }
    }
    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 with the existing helper and generated fixtures.", SkipUnless = nameof(NativeRequested))]
    public async Task CancelingAStartedHelperKillsItAndPublishesNoPcm()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); var envelope = new RmsEnvelope(); int? child = null;
        var decoder = new FfmpegEnvelopeDecoder(Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_TEST_PATH"), process =>
        { child = process.Id; stop.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => decoder.Decode(
            Path.Combine(Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_FIXTURES")!, "known.wav"), new(envelope), stop.Token), TestContext.Current.CancellationToken));
        Assert.NotNull(child); Assert.False(envelope.IsReady);
        try { using var remaining = Process.GetProcessById(child.Value); Assert.True(remaining.HasExited); }
        catch (ArgumentException) { }
    }
    private sealed class Pieces(byte[] data, Action? afterRead = null) : MemoryStream(data)
    {
        public int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++; var read = base.ReadAsync(buffer[..Math.Min(buffer.Length, Reads % 3 * 2 + 1)], cancellationToken);
            afterRead?.Invoke(); return read;
        }
    }
}
