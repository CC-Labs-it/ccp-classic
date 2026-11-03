using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ProductionRuntimeTests
{
    [Fact]
    public void VideoOnlyAndCompanionOnlySessionsKeepTheNativeFrameCadence()
    {
        var settings = new ClassicSettings();
        foreach (var effect in settings.Effects.Values) effect.Enabled = false;
        Assert.Equal(TimeSpan.FromMilliseconds(250), DesktopEffectsService.FrameInterval(settings, false, false));
        Assert.True(DesktopEffectsService.FrameInterval(settings, true, false).TotalMilliseconds < 34);
        settings.CompanionEnabled = true;
        Assert.True(DesktopEffectsService.FrameInterval(settings, false, false).TotalMilliseconds < 34);
        settings.PerformanceMode = true;
        Assert.InRange(DesktopEffectsService.FrameInterval(settings, false, false).TotalMilliseconds, 66, 67);
    }

    [Fact]
    public Task PresetPickerStartProgressAndPanicAreConnected() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-profile-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(presetStore: new PresetStore(folder));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var picker = Named<ComboBox>(window, "PresetPicker");
            picker.SelectedItem = ((IEnumerable<PresetDefinition>)picker.ItemsSource!).Single(item => item.Name == "Gentle");
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Settings.GentleVisuals);
            Assert.True(Named<ToggleButton>(window, "GentleToggle").IsChecked);
            var start = Named<Button>(window, "StartStop");
            var point = start.TranslatePoint(new Point(start.Bounds.Width / 2, start.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.True(window.Session.IsRunning); Assert.True(window.Progress.IsRunning);
            window.Progress.BubblePopped(); window.Progress.VideoFinished(10);
            window.SelectTab("Progress"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("1", Named<TextBlock>(window, "ProgressStat_BubblesPopped").Text);
            Assert.Equal("1", Named<TextBlock>(window, "ProgressStat_VideosFinished").Text);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.False(window.Session.IsRunning); Assert.False(window.Progress.IsRunning);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public Task NativeInteractiveLayersRenderAndStopWithoutGhosts() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings { GentleVisuals = true };
        settings.Effects["bounce"].Enabled = settings.Effects["drain"].Enabled = true;
        var interactive = new InteractiveEffectsTimeline(settings); var locks = new LockEffectsTimeline(settings);
        interactive.Start(); locks.Start();
        InteractiveEffectsFrame frame = InteractiveEffectsFrame.Empty; LockEffectsFrame locked = LockEffectsFrame.Empty;
        for (var index = 0; index <= 270; index++) { frame = interactive.Step(index / 30d); locked = locks.Step(index / 30d); }
        Assert.NotEmpty(frame.Bubbles); Assert.NotNull(frame.BouncingText); Assert.NotNull(locked.BrainDrain);
        var view = new VisualEffectsControl { Width = 640, Height = 360 };
        view.UpdateFrame(new EffectFrame { Interactive = frame, BrainDrain = locked.BrainDrain });
        using var rendered = Render(view);
        Assert.True(AlphaPixels(rendered).Count(alpha => alpha != 0) > 200);
        if (Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR") is { } folder)
        { Directory.CreateDirectory(folder); rendered.Save(Path.Combine(folder, "interactive-effects.png"), PngBitmapEncoderOptions.Default); }
        interactive.Stop(); locks.Stop();
        view.UpdateFrame(new EffectFrame { Interactive = interactive.Step(10), BrainDrain = locks.Step(10).BrainDrain });
        using var cleared = Render(view); Assert.All(AlphaPixels(cleared), alpha => Assert.Equal(0, alpha));
    });

    [Fact]
    public Task FadingBouncingTextPreservesTheFullGlyphShape() => UiDispatcher.Run(() =>
    {
        var view = new VisualEffectsControl { Width = 640, Height = 360 };
        view.UpdateFrame(new EffectFrame { Interactive = new([], new("Breathe", .5, .5, .5, 1)) });
        using var opaque = Render(view);
        var full = AlphaPixels(opaque);
        view.UpdateFrame(new EffectFrame { Interactive = new([], new("Breathe", .5, .5, .5, .4)) });
        using var faded = Render(view);
        var partial = AlphaPixels(faded);
        var solid = Enumerable.Range(0, full.Length).Where(index => full[index] >= 250).ToArray();
        Assert.True(solid.Length > 200);
        Assert.All(solid, index => Assert.InRange(partial[index], (byte)95, (byte)106));
    });

    private static RenderTargetBitmap Render(Control view)
    {
        view.Measure(new Size(640, 360)); view.Arrange(new Rect(0, 0, 640, 360));
        var bitmap = new RenderTargetBitmap(new PixelSize(640, 360), new Vector(96, 96)); bitmap.Render(view); return bitmap;
    }
    private static byte[] AlphaPixels(Bitmap bitmap)
    {
        var bytes = new byte[640 * 360 * 4]; var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 640, 360), handle.AddrOfPinnedObject(), bytes.Length, 640 * 4); }
        finally { handle.Free(); }
        return bytes.Where((_, index) => index % 4 == 3).ToArray();
    }
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(item => item.Name == name);
}
