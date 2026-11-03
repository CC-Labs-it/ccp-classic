using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Input;
using CCP.Classic.App.Media;
using LibVLCSharp.Shared;

namespace CCP.Classic.Probe;

internal static class Program
{
    private static string stage = "private_display_guard";
    private static CancellationToken token;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            Check(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CCP_CLASSIC_NATIVE_PROBE") == "1"
                && NativePanicKeys.IsLocalDisplay(display)
                && display == Environment.GetEnvironmentVariable("CCP_CLASSIC_PRIVATE_DISPLAY")
                && File.Exists(Environment.GetEnvironmentVariable("XAUTHORITY")), "private_display_required");
            Check(args.Length == 1 && args[0] == "--no-compositor" || args.Length == 2 && args[0] == "--media", "arguments");
            using var total = new CancellationTokenSource(TimeSpan.FromSeconds(60)); token = total.Token;
            AppBuilder.Configure<ProbeApplication>().UsePlatformDetect()
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software], EnableSessionManagement = false })
                .SetupWithoutStarting();
            var result = 1; var finished = false;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Run(args); Pass("complete"); result = 0;
                }
                catch (Exception failure) { Fail(failure); }
                finally { finished = true; total.Cancel(); }
            });
            try { Dispatcher.UIThread.MainLoop(token); }
            catch (OperationCanceledException) when (total.IsCancellationRequested) { }
            if (!finished) Fail(new ProbeFailure("total_timeout"));
            return result;
        }
        catch (Exception failure) { Fail(failure); return 1; }
    }

    private static async Task Run(string[] args)
    {
        var display = XOpenDisplay(Environment.GetEnvironmentVariable("DISPLAY")!);
        Check(display != 0, "display_open");
        try
        {
            var selection = XInternAtom(display, "_NET_WM_CM_S" + XDefaultScreen(display), false);
            if (args[0] == "--no-compositor")
            {
                stage = "no_compositor_refusal";
                Check(XGetSelectionOwner(display, selection) == 0, "unexpected_compositor");
                var rejected = new EffectOverlayWindow();
                try
                {
                    var screen = await ProbeScreen(rejected);
                    Check(!rejected.Prepare(screen) && !rejected.IsVisible && rejected.LastError is not null
                        && rejected.Width <= 1 && rejected.Height <= 1, "unsafe_refusal");
                    Pass(stage);
                }
                finally { rejected.Close(); }
                return;
            }
            stage = "compositor_ready";
            await Until(() => XGetSelectionOwner(display, selection) != 0, 5);
            stage = "x11_transparency_and_input_shape";
            var overlay = new EffectOverlayWindow();
            try
            {
                var screen = await ProbeScreen(overlay);
                Check(overlay.Prepare(screen) && overlay.IsVisible, "overlay_prepare");
                var handle = overlay.TryGetPlatformHandle() ?? throw new ProbeFailure("native_handle");
                Check(handle.HandleDescriptor == "XID" && overlay.ActualTransparencyLevel == WindowTransparencyLevel.Transparent,
                    "native_transparency");
                var xids = Windows(display, handle.Handle);
                foreach (var xid in xids) Shape(display, xid, []);
                var input = new Rect(.25, .25, .25, .25);
                var expected = NativeOverlayInput.PixelBounds([input], PixelSize.FromSize(overlay.ClientSize, overlay.RenderScaling));
                Check(overlay.SetInputBounds([input]), "shape_set");
                foreach (var xid in xids) Shape(display, xid, expected);
                Check(overlay.SetInputBounds([]), "shape_clear");
                foreach (var xid in xids) Shape(display, xid, []);
                Pass(stage, new { windows = xids.Length, regions = expected.Length });
                await Input(display);
            }
            finally { overlay.View.UpdateFrame(EffectFrame.Empty); overlay.Close(); }
            await Video(Path.Combine(Path.GetFullPath(args[1]), "neutral.mp4"));
            await Audio(Path.Combine(Path.GetFullPath(args[1]), "silent.wav"));
            await Analysis(Path.Combine(Path.GetFullPath(args[1]), "silent.wav"));
        }
        finally { XCloseDisplay(display); }
    }

    private static async Task<Avalonia.Platform.Screen> ProbeScreen(Window window)
    {
        await Until(() => window.Screens.All.Any(screen => screen.Bounds.Width > 0 && screen.Bounds.Height > 0), 5);
        // Xvfb can expose a valid RandR monitor without marking any monitor primary.
        var screen = window.Screens.Primary ?? window.Screens.All.First(item => item.Bounds.Width > 0 && item.Bounds.Height > 0);
        Pass("display_available", new { count = window.Screens.ScreenCount, hasPrimary = window.Screens.Primary is not null,
            width = screen.Bounds.Width, height = screen.Bounds.Height });
        return screen;
    }

    private static async Task Input(nint display)
    {
        stage = "x11_global_input";
        var downs = 0; var ups = 0; var letters = 0;
        using var monitor = new GlobalPanicMonitor(() => "Escape", (name, down) =>
        { if (name == "Escape") { if (down) downs++; else ups++; } }, () => true);
        monitor.TextReceived += text => { if (text == "f") letters++; };
        await Until(() => monitor.IsAvailable && monitor.IsTextAvailable, 5);
        var escape = XKeysymToKeycode(display, XStringToKeysym("Escape"));
        var letter = XKeysymToKeycode(display, XStringToKeysym("f"));
        Check(escape != 0 && letter != 0, "key_mapping");
        try
        {
            Key(display, escape, true); await Until(() => downs == 1, 2);
            await Task.Delay(150, token); Check(downs == 1, "held_repeat");
            Key(display, escape, false); await Until(() => ups == 1, 2);
            Key(display, letter, true); await Until(() => letters == 1, 2);
            await Task.Delay(100, token); Check(letters == 1, "text_held_repeat");
            Key(display, letter, false);
            monitor.Dispose(); Check(!monitor.IsTextAvailable, "listener_disposal");
            Pass(stage, new { down = downs, up = ups, keywordEvents = letters });
        }
        finally { Key(display, escape, false); Key(display, letter, false); }
    }

    private static async Task Video(string path)
    {
        stage = "native_muted_video";
        var video = new LocalVideoPlayback(path, 10); var frames = 0; var colored = false; float? rate = null;
        var elapsed = Stopwatch.StartNew(); var samples = new List<object>();
        double? lastSource = null; bool? lastPlaying = null;
        try
        {
            video.SetGain(0); video.Start();
            await Until(() =>
            {
                if (video.TryCopyFrame(out var frame) && frame is not null)
                {
                    Check(frame.Width == 320 && frame.Height == 180 && frame.Pixels.Length == 320 * 180 * 4, "frame_dimensions");
                    for (var index = 0; index < frame.Pixels.Length; index += 4)
                        colored |= frame.Pixels[index] + frame.Pixels[index + 1] + frame.Pixels[index + 2] > 0;
                    frames++; rate = Rate(video) ?? rate;
                }
                var sample = video.GetAudioSample();
                if (samples.Count == 0 || sample?.SourceSeconds != lastSource || sample?.IsPlaying != lastPlaying)
                {
                    var observation = new { elapsedMs = elapsed.ElapsedMilliseconds, sourceSeconds = sample?.SourceSeconds,
                        playing = sample?.IsPlaying, playedSeconds = video.PlayedSeconds, frames };
                    if (samples.Count < 32) samples.Add(observation);
                    else samples[^1] = observation;
                    lastSource = sample?.SourceSeconds; lastPlaying = sample?.IsPlaying;
                }
                return video.IsFinished;
            }, 15);
            Pass("video_completion_evidence", new { frames, colored, rate, playedSeconds = video.PlayedSeconds,
                hasError = video.LastError is not null, samples });
            Check(frames > 0 && colored && rate == 1f && video.LastError is null && video.PlayedSeconds >= 1, "video_completion");
            Pass(stage, new { frames, width = 320, height = 180, rate, playedSeconds = video.PlayedSeconds });
        }
        finally { video.Dispose(); await video.Completion.WaitAsync(TimeSpan.FromSeconds(5), token); }
        stage = "native_video_live_cap";
        var capped = new LocalVideoPlayback(path, 1); var capClock = Stopwatch.StartNew(); long? firstFrame = null;
        try
        {
            capped.SetGain(0); capped.Start();
            await Until(() =>
            {
                if (capped.TryCopyFrame(out _)) firstFrame ??= capClock.ElapsedMilliseconds;
                return capped.IsFinished;
            }, 10);
            var presentedMs = firstFrame is { } first ? capClock.ElapsedMilliseconds - first : -1;
            Pass("video_cap_evidence", new { presentedMs, playedSeconds = capped.PlayedSeconds, hasError = capped.LastError is not null });
            Check(firstFrame is not null && presentedMs is >= 750 and <= 1500
                && capped.PlayedSeconds is >= .99 and <= 1 && capped.LastError is null, "video_live_cap");
            Pass(stage);
        }
        finally { capped.Dispose(); await capped.Completion.WaitAsync(TimeSpan.FromSeconds(5), token); }
        stage = "native_video_cancel";
        var canceled = new LocalVideoPlayback(path, 10);
        try
        {
            canceled.SetGain(0); canceled.Start();
            await Until(() => canceled.TryCopyFrame(out _), 10);
            canceled.Dispose(); Check(canceled.IsFinished, "video_cancel_immediate");
        }
        finally { canceled.Dispose(); await canceled.Completion.WaitAsync(TimeSpan.FromSeconds(5), token); }
        Check(canceled.LastError is null, "video_cancel_error"); Pass(stage);
    }

    private static async Task Audio(string path)
    {
        stage = "native_muted_audio";
        var audio = new LibVlcAudioPlayback();
        try
        {
            audio.SetMasterGain(0);
            var ticket = audio.PlayTracked(path, 1) ?? throw new ProbeFailure("audio_request");
            await Until(() => ticket.IsFinished, 10);
            Check(ticket.WasSuccessful && Math.Abs(ticket.PlayedSeconds - 2) < .1 && audio.LastError is null, "audio_completion");
            Pass(stage, new { playedSeconds = ticket.PlayedSeconds });
            stage = "native_audio_cancel";
            var canceled = audio.PlayTracked(path, 1) ?? throw new ProbeFailure("audio_cancel_request");
            await Until(() => canceled.PlayedSeconds > .02 || canceled.IsFinished, 5);
            Check(!canceled.IsFinished, "audio_cancel_active"); audio.StopAll();
            Check(canceled.IsFinished && !canceled.WasSuccessful, "audio_cancel_immediate"); Pass(stage);
        }
        finally { audio.Dispose(); await audio.Completion.WaitAsync(TimeSpan.FromSeconds(5), token); }
    }

    private static async Task Analysis(string path)
    {
        stage = "native_offline_analysis";
        using var cache = new AudioEnvelopeCache();
        var envelope = cache.Get(path);
        await cache.Completion.WaitAsync(TimeSpan.FromSeconds(15), token);
        Check(!envelope.Failed && envelope.IsComplete && envelope.DurationSeconds is > 1.99 and < 2.01
            && envelope.Sample(1) == 0, "analysis_source_time");
        Pass(stage, new { duration = envelope.DurationSeconds, rms = envelope.Sample(1) });
        stage = "native_analysis_cancel";
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var canceled = false;
        var decoder = new FfmpegEnvelopeDecoder(started: _ => stop.Cancel());
        try { await Task.Run(() => decoder.Decode(path, new(new RmsEnvelope()), stop.Token), token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { canceled = true; }
        Check(canceled, "analysis_cancel_immediate"); Pass(stage);
    }

    private static float? Rate(LocalVideoPlayback playback)
    {
        try
        {
            var device = typeof(LocalVideoPlayback).GetField("device", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(playback);
            return (device?.GetType().GetField("player", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(device) as MediaPlayer)?.Rate;
        }
        catch (ObjectDisposedException) { return null; }
    }
    private static async Task Until(Func<bool> ready, int seconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready())
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.Elapsed.TotalSeconds >= seconds) throw new ProbeFailure("stage_timeout");
            await Task.Delay(20, token);
        }
    }
    private static nint[] Windows(nint display, nint window)
    {
        Check(XQueryTree(display, window, out _, out _, out var children, out var count) != 0, "window_tree");
        try
        {
            Check(count <= 32, "window_tree_budget");
            return new[] { window }.Concat(Enumerable.Range(0, (int)count).Select(index => Marshal.ReadIntPtr(children, index * IntPtr.Size))).ToArray();
        }
        finally { if (children != 0) XFree(children); }
    }
    private static void Shape(nint display, nint window, PixelRect[] expected)
    {
        var rectangles = XShapeGetRectangles(display, window, 2, out var count, out _);
        try
        {
            Check(count == expected.Length, "input_region_count");
            for (var index = 0; index < count; index++)
            {
                var actual = Marshal.PtrToStructure<XRectangle>(rectangles + index * 8); var wanted = expected[index];
                Check(actual.X == wanted.X && actual.Y == wanted.Y && actual.Width == wanted.Width && actual.Height == wanted.Height,
                    "input_region_bounds");
            }
        }
        finally { if (rectangles != 0) XFree(rectangles); }
    }
    private static void Key(nint display, uint key, bool down)
    { Check(XTestFakeKeyEvent(display, key, down ? 1 : 0, 0) != 0, "private_key_event"); XSync(display, false); }
    private static void Check(bool condition, string code) { if (!condition) throw new ProbeFailure(code); }
    private static void Pass(string name, object? evidence = null) => Console.WriteLine(JsonSerializer.Serialize(new { stage = name, result = "PASS", evidence }));
    private static void Fail(Exception failure) => Console.Error.WriteLine(JsonSerializer.Serialize(new
    { stage, result = "FAIL", code = failure is ProbeFailure known ? known.Message : failure.GetType().Name }));
    private sealed class ProbeFailure(string code) : Exception(code);
    private sealed class ProbeApplication : Application { public ProbeApplication() { } }
    [StructLayout(LayoutKind.Sequential)] private struct XRectangle { public short X, Y; public ushort Width, Height; }
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern nint XGetSelectionOwner(nint display, nint atom);
    [DllImport("libX11.so.6")] private static extern int XQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint count);
    [DllImport("libX11.so.6")] private static extern int XFree(nint memory);
    [DllImport("libX11.so.6")] private static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport("libX11.so.6")] private static extern int XSync(nint display, bool discard);
    [DllImport("libXext.so.6")] private static extern nint XShapeGetRectangles(nint display, nint window, int kind, out int count, out int ordering);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(nint display, uint key, int down, nuint delay);
}
