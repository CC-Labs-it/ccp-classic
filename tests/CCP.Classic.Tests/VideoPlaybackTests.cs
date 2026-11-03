using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class VideoPlaybackTests
{
    private static Task Until(Func<bool> condition) => Task.Run(() =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5))), TestContext.Current.CancellationToken);

    [Fact]
    public async Task NetworkAndMissingPathsNeverOpenTheNativeDevice()
    {
        foreach (var path in new[] { "https://example.invalid/video.mp4", @"\\server\share\video.mp4", "//server/video.mp4", "missing.mp4" })
        {
            var attempts = 0;
            using var playback = new LocalVideoPlayback(path, 60, _ => { attempts++; return new Device(); });
            playback.Start();
            await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, attempts);
            Assert.True(playback.IsFinished);
            Assert.NotNull(playback.LastError);
        }
    }

    [Fact]
    public async Task PanicDuringNativeOpeningCannotStartTheLateDevice()
    {
        using var file = new VideoFile();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = new Device();
        var playback = new LocalVideoPlayback(file.Path, 60, _ =>
        {
            entered.Set(); release.Wait(TestContext.Current.CancellationToken); return device;
        });
        try
        {
            playback.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            playback.Dispose();
            Assert.True(playback.IsFinished);
            Assert.False(playback.TryCopyFrame(out _));
            release.Set();
            await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, device.Starts);
            Assert.True(device.Disposed);
        }
        finally { release.Set(); playback.Dispose(); }
    }

    [Fact]
    public async Task OpeningUsesTheLatestGainAndPauseAndStartsOnlyOnce()
    {
        using var file = new VideoFile();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = new Device();
        var playback = new LocalVideoPlayback(file.Path, 60, _ =>
        {
            entered.Set(); release.Wait(TestContext.Current.CancellationToken); return device;
        });
        try
        {
            playback.Start(); playback.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            playback.SetGain(.37); playback.SetPaused(true);
            release.Set();
            await Until(() => device.Starts == 1 && device.Paused);
            Assert.Equal(.37, device.StartGain);
            playback.SetGain(double.NaN); playback.SetPaused(false);
            await Until(() => device.Gain == 0 && !device.Paused);
            playback.SetGain(2);
            await Until(() => device.Gain == 1);
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task MeterUsesTheSourcePositionAndHidesPausedOrDisposedPlaybackImmediately()
    {
        using var file = new VideoFile(); var device = new Device { SourcePosition = 12000 };
        var playback = new LocalVideoPlayback(file.Path, 60, _ => device);
        try
        {
            playback.SetGain(.3); playback.Start();
            await Until(() => playback.GetAudioSample() is { SourceSeconds: 12, Gain: .3, IsPlaying: true });
            Assert.Equal(0, playback.PlayedSeconds); playback.SetPaused(true); Assert.Null(playback.GetAudioSample());
            playback.SetPaused(false); await Until(() => playback.GetAudioSample() is { IsPlaying: true });
            playback.Dispose(); Assert.Null(playback.GetAudioSample());
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task DisposalHidesFramesImmediatelyWhileNativeTeardownIsBlocked()
    {
        using var file = new VideoFile();
        using var stopping = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = new Device(() => { stopping.Set(); release.Wait(TestContext.Current.CancellationToken); });
        var playback = new LocalVideoPlayback(file.Path, 60, _ => device);
        try
        {
            playback.Start(); await Until(() => device.Starts == 1);
            device.Frame = new([1, 2, 3, 255], 1, 1, 1);
            Assert.True(playback.TryCopyFrame(out var first));
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, first!.Pixels);
            Assert.False(playback.TryCopyFrame(out _));
            device.Frame = new([4, 5, 6, 255], 1, 1, 2);
            await Task.Run(playback.Dispose, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(stopping.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(playback.TryCopyFrame(out _));
            Assert.False(playback.Completion.IsCompleted);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, first.Pixels);
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task DecoderFailureReportsStatusAndReleasesTheDevice()
    {
        using var file = new VideoFile();
        var device = new Device(refresh: () => throw new IOException("decoder unavailable"));
        using var playback = new LocalVideoPlayback(file.Path, 60, _ => device);
        playback.Start();
        await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(playback.IsFinished);
        Assert.True(device.Disposed);
        Assert.Equal("decoder unavailable", playback.LastError);
    }

    [Fact]
    public async Task APlaylistDisguisedAsVideoFailsBeforeNativeInitialization()
    {
        using var file = new VideoFile();
        File.WriteAllText(file.Path, "#EXTM3U\nhttps://example.invalid/video.mp4");
        using var playback = new LocalVideoPlayback(file.Path, 60);
        playback.Start();
        await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Unsupported video container.", playback.LastError);
        Assert.False(playback.TryCopyFrame(out _));
    }

    [Fact]
    public void AVideoBehindASymbolicLinkIsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        using var file = new VideoFile();
        var link = file.Path + ".link.mp4";
        File.CreateSymbolicLink(link, file.Path);
        try { Assert.Null(LocalVideoPlayback.LocalPath(link)); }
        finally { File.Delete(link); }
    }

    [Fact]
    public void CapCountsPlaybackButExcludesBufferingPausesAndSeekDiscontinuities()
    {
        var allowance = new VideoPlaybackAllowance();
        allowance.Observe(5_000, 0, true);
        allowance.Observe(5_000, 250, true);
        allowance.Observe(5_500, 500, true);
        allowance.Observe(5_500, 1_000, false);
        allowance.Observe(5_500, 2_000, true);
        allowance.Observe(6_000, 2_500, true);
        allowance.Observe(200_000, 2_600, true);
        allowance.Observe(200_500, 3_100, true);
        allowance.Observe(1_000, 3_200, true);
        allowance.Observe(1_500, 3_700, true);
        Assert.Equal(2_000, allowance.PlayedMilliseconds);
    }

    [Theory]
    [InlineData(1920u, 1080u, 960, 540)]
    [InlineData(1080u, 1920u, 304, 540)]
    [InlineData(2048u, 2048u, 540, 540)]
    [InlineData(320u, 240u, 320, 240)]
    [InlineData(0u, 240u, 0, 0)]
    public void FramesStayBoundedWithoutStretching(uint sourceWidth, uint sourceHeight, int width, int height)
        => Assert.Equal((width, height), LocalVideoPlayback.FrameSize(sourceWidth, sourceHeight));

    [Fact]
    public void AnamorphicAndRotatedVideoUseTheirDisplayAspectRatio()
    {
        Assert.Equal((960, 540), LocalVideoPlayback.FrameSize(720, 576, 64, 45));
        Assert.Equal((304, 540), LocalVideoPlayback.FrameSize(1920, 1080, rotated: true));
    }

    [Fact]
    public void RenamedPlaylistsAreRejectedAndNativeOptionsCannotAutoSelectTheirDemuxer()
    {
        Assert.Null(LocalVideoPlayback.Demux("#EXTM3U\nhttps://example.invalid/movie.mp4"u8));
        Assert.Null(LocalVideoPlayback.Demux("<html>https://example.invalid/movie.mp4</html>"u8));
        Assert.Equal("mp4", LocalVideoPlayback.Demux(new byte[] { 0, 0, 0, 16, 102, 116, 121, 112, 105, 115, 111, 109 }));
        Assert.Equal("mkv", LocalVideoPlayback.Demux(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
        Assert.Equal("avi", LocalVideoPlayback.Demux("RIFF1234AVI "u8));
        var options = LocalVideoPlayback.Options(true, "mp4");
        Assert.Contains("--demux=mp4,none", options);
        Assert.Contains("--aout=directsound", options);
        Assert.Contains("--rate=1", options);
        Assert.Contains("--no-metadata-network-access", options);
        Assert.Contains("--no-sub-autodetect-file", options);
        Assert.Contains("--no-mkv-use-chapter-codec", options);
        Assert.DoesNotContain(LocalVideoPlayback.Options(false, "mkv"), option => option.StartsWith("--aout=", StringComparison.Ordinal));
    }

    private sealed class Device(Action? dispose = null, Action? refresh = null) : ILocalVideoDevice
    {
        private int starts, disposed, paused;
        private double gain;
        public VideoFrame? Frame;
        public long SourcePosition;
        public int Starts => Volatile.Read(ref starts);
        public bool Disposed => Volatile.Read(ref disposed) != 0;
        public bool Paused => Volatile.Read(ref paused) != 0;
        public double Gain => Volatile.Read(ref gain);
        public double StartGain { get; private set; }
        public int Width => 1;
        public int Height => 1;
        public bool IsFinished => false;
        public bool IsPlaying => !Paused;
        public long PositionMilliseconds => Interlocked.Read(ref SourcePosition);
        public void Start() { StartGain = Gain; Interlocked.Increment(ref starts); }
        public void SetGain(double value) => Volatile.Write(ref gain, value);
        public void SetPaused(bool value) => Interlocked.Exchange(ref paused, value ? 1 : 0);
        public bool TryCopyFrame(out VideoFrame? frame) { frame = Interlocked.Exchange(ref Frame, null); return frame is not null; }
        public void Refresh() => refresh?.Invoke();
        public void Dispose() { dispose?.Invoke(); Interlocked.Exchange(ref disposed, 1); }
    }
    private sealed class VideoFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
        public VideoFile() => File.WriteAllBytes(Path, [0, 0, 0, 16, 102, 116, 121, 112, 105, 115, 111, 109]);
        public void Dispose() => File.Delete(Path);
    }
}
