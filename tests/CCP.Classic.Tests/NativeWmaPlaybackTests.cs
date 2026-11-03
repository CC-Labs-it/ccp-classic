using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CCP.Classic.App.Media;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class NativeWmaPlaybackTests
{
    public static bool NativeRequested => OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_PROBE") == "1";

    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 with the verified helper and ignored fixture directory.", SkipUnless = nameof(NativeRequested))]
    public async Task GeneratedWmaPlaysAtZeroGainAndItsAnalysisPreservesSilenceAndBothEnergyLevels()
    {
        var token = TestContext.Current.CancellationToken;
        var helper = Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_TEST_PATH")!;
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_FIXTURES")!;
        Assert.Equal(helper, AnalysisHelper.VerifyBundle(Path.GetDirectoryName(helper)!, "win-x64", "ffmpeg.exe", token));
        Directory.CreateDirectory(folder); var path = await Generate(helper, folder, token);
        using (var input = new LocalAudioInput(path)) Assert.Equal("asf", input.Demux);
        var envelope = new RmsEnvelope();
        await Task.Run(() => new FfmpegEnvelopeDecoder(helper).Decode(path, new(envelope), token), token);
        Assert.InRange(envelope.DurationSeconds, 1.5, 1.8);
        Assert.InRange(envelope.Sample(.025), 0, .001);
        Assert.InRange(envelope.Sample(.425), .45, .55); Assert.InRange(envelope.Sample(1.025), .22, .28);
        Assert.InRange(envelope.Sample(.425) / envelope.Sample(1.025), 1.8, 2.2);
        await Task.Run(async () =>
        {
            using var device = new NativeAudioDevice(); using var voice = device.Open(path, 0, () => { });
            var player = Assert.IsType<MediaPlayer>(voice.GetType().GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice));
            var wall = Stopwatch.StartNew(); var observedPlaying = false; var sourceSeconds = 0d;
            voice.SetGain(0); voice.Start();
            while (!voice.IsFinished && wall.Elapsed < TimeSpan.FromSeconds(10))
            {
                voice.Refresh(); Assert.Equal(0, player.Volume);
                observedPlaying |= voice.IsPlaying; sourceSeconds = Math.Max(sourceSeconds, voice.SourceSeconds);
                await Task.Delay(10, token);
            }
            voice.Refresh(); Assert.True(voice.IsFinished); Assert.True(voice.WasSuccessful);
            Assert.True(observedPlaying); Assert.True(sourceSeconds > .3);
            Assert.InRange(voice.PlayedSeconds, 1.4, 1.9); Assert.InRange(wall.Elapsed.TotalSeconds, 1.1, 6);
            File.WriteAllText(Path.Combine(folder, "native-wma-playback.json"), JsonSerializer.Serialize(new
            {
                format = "ASF/WMA2", gain = 0, sourceSeconds, playedSeconds = voice.PlayedSeconds,
                wallSeconds = wall.Elapsed.TotalSeconds, leadingSilence = envelope.Sample(.025),
                highRms = envelope.Sample(.425), lowRms = envelope.Sample(1.025), analysisSeconds = envelope.DurationSeconds
            }));
        }, token);
    }

    private static async Task<string> Generate(string helper, string folder, CancellationToken token)
    {
        var wave = Path.Combine(folder, "neutral-wma-source.wav"); var result = Path.Combine(folder, "neutral-audio.wma");
        const int rate = 48000, frames = rate * 8 / 5;
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
            writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(frames * 4);
            for (var frame = 0; frame < frames; frame++)
            {
                var time = frame / (double)rate;
                var amplitude = time < .2 || time >= 1.4 ? 0 : time < .8 ? Math.Sqrt(.5) : Math.Sqrt(.125);
                var sample = (short)Math.Round(Math.Sin(time * Math.Tau * 440) * amplitude * 32767);
                writer.Write(sample); writer.Write(sample);
            }
        }
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-v", "error", "-y", "-threads", "2", "-filter_threads", "1",
            "-max_alloc", "67108864", "-protocol_whitelist", "file,pipe", "-f", "wav", "-i", wave,
            "-map", "0:a:0", "-vn", "-sn", "-dn", "-c:a", "wmav2", "-b:a", "128k", "-threads", "2", "-f", "asf", result }) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info }; using var budget = new AnalysisProcessBudget();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); stop.CancelAfter(TimeSpan.FromSeconds(10));
        AnalysisProcessBudget.RequireHeadroom(); Assert.True(process.Start());
        try
        {
            budget.Attach(process); using var cancel = stop.Token.Register(budget.Stop);
            var errors = process.StandardError.ReadToEndAsync(stop.Token);
            while (!process.HasExited) { stop.Token.ThrowIfCancellationRequested(); budget.Check(); await Task.Delay(25, stop.Token); }
            await process.WaitForExitAsync(stop.Token); Assert.Equal(0, process.ExitCode); Assert.Empty(await errors);
        }
        finally { budget.Stop(); }
        return result;
    }
}
