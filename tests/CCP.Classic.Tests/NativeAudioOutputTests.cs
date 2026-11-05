using System.Diagnostics;
using System.Reflection;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class NativeAudioOutputTests
{
    public static bool NativeRequested => OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_PROBE") == "1";

    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 on a Windows desktop with a local output.", SkipUnless = nameof(NativeRequested))]
    public async Task RealOutputEnumerationAndMutedPlaybackSurviveDeviceSelectionChanges()
    {
        var outputs = await AudioOutputSelection.EnumerateAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(outputs);
        var path = new StarterPack().Phrases.Single(phrase => phrase.Text.Equals("relax", StringComparison.OrdinalIgnoreCase)).AudioPath;
        Assert.NotNull(path);
        AudioOutputSelection.Configure(new() { OutputDeviceId = outputs[0].Id });
        try
        {
            using var device = new NativeAudioDevice();
            using var voice = device.Open(path, 0, () => { });
            var player = Assert.IsType<MediaPlayer>(voice.GetType().GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice));
            voice.Start(); var elapsed = Stopwatch.StartNew(); var changed = false;
            while (!voice.IsFinished && elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                voice.Refresh();
                if (!changed && voice.IsPlaying && elapsed.ElapsedMilliseconds >= 100)
                {
                    Assert.Equal(outputs[0].Id, player.OutputDevice, ignoreCase: true);
                    AudioOutputSelection.Configure(new AudioOptions());
                    voice.Refresh(); Assert.True(string.IsNullOrEmpty(player.OutputDevice)); changed = true;
                }
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            voice.Refresh(); Assert.True(changed); Assert.True(voice.IsFinished);
            Assert.True(voice.WasSuccessful); Assert.True(voice.PlayedSeconds > 0);
            voice.Stop();
        }
        finally { AudioOutputSelection.Configure(new AudioOptions()); }
    }
}
