using System.Diagnostics;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class NativeVideoPlaybackTests
{
    public static bool NativeRequested => OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_PROBE") == "1";

    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 with the silent neutral-video.mp4 fixture.", SkipUnless = nameof(NativeRequested))]
    public async Task SilentNativeVideoKeepsItsPacingAndHonorsTheLiveCap()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_FIXTURES")!, "neutral-video.mp4");
        Assert.True(File.Exists(path));
        foreach (var cap in new[] { 10d, 1d })
        {
            var playback = new LocalVideoPlayback(path, cap); var clock = Stopwatch.StartNew();
            double? firstFrame = null; var frames = 0;
            try
            {
                playback.SetGain(0); playback.Start();
                while (!playback.IsFinished && clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    if (playback.TryCopyFrame(out var frame) && frame is not null)
                    {
                        firstFrame ??= clock.Elapsed.TotalSeconds; frames++;
                        Assert.Equal(320, frame.Width); Assert.Equal(180, frame.Height);
                        Assert.True(frame.Pixels[0] + frame.Pixels[1] + frame.Pixels[2] > 0);
                    }
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.True(playback.IsFinished); Assert.Null(playback.LastError);
                Assert.NotNull(firstFrame); Assert.True(frames > 5);
                var visibleSeconds = clock.Elapsed.TotalSeconds - firstFrame.Value;
                if (cap == 1)
                {
                    Assert.InRange(playback.PlayedSeconds, .95, 1.3);
                    Assert.InRange(visibleSeconds, .85, 1.75);
                }
                else
                {
                    Assert.InRange(playback.PlayedSeconds, 1.5, 2.3);
                    Assert.InRange(visibleSeconds, 1.5, 6);
                }
            }
            finally
            {
                playback.Dispose();
                await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
        }
    }
}
