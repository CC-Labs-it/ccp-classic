using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class FirstPlayableTests
{
    private const int Width = 960, Height = 540;

    [Fact]
    public Task StarterTimelineBuildsAComposedFrameAndStopLeavesNoGhosts() => UiDispatcher.Run(() =>
    {
        var starter = new StarterPack();
        Assert.NotEmpty(starter.Images);
        Assert.NotEmpty(starter.Phrases);
        var images = starter.Images.Select(path => new Bitmap(path)).ToArray();
        try
        {
            var settings = new ClassicSettings { GentleVisuals = true };
            var timeline = new EffectsTimeline(settings, images.Length, starter.Phrases.Count);
            timeline.Start();
            var snapshot = timeline.Step(0);
            for (var step = 1; step <= 75; step++) snapshot = timeline.Step(step / 30d);
            var frame = EffectsFrameBuilder.Build(snapshot, settings, images, starter.Phrases);
            Assert.Contains(frame.Flashes, flash => flash.Opacity > 0);
            Assert.True(frame.SubliminalOpacity > 0);
            Assert.Contains(starter.Phrases, phrase => phrase.Text == frame.SubliminalText);
            Assert.True(frame.PinkOpacity > 0 && frame.SpiralOpacity > 0);
            var view = new VisualEffectsControl { Width = Width, Height = Height };
            view.UpdateFrame(frame);
            using var rendered = Render(view);
            Assert.Contains(Pixels(rendered), pixel => pixel >>> 24 > 0);
            var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                var preview = new Border { Background = new SolidColorBrush(Color.Parse("#121220")),
                    Child = new Image { Source = rendered, Stretch = Stretch.Fill }, Width = Width, Height = Height };
                using var proof = Render(preview);
                proof.Save(Path.Combine(folder, "first-playable.png"), PngBitmapEncoderOptions.Default);
            }
            timeline.Stop();
            var stopped = EffectsFrameBuilder.Build(timeline.Step(3), settings, images, starter.Phrases);
            Assert.Empty(stopped.Flashes);
            Assert.Null(stopped.SubliminalText);
            view.UpdateFrame(stopped);
            using var cleared = Render(view);
            Assert.All(Pixels(cleared), pixel => Assert.Equal(0, pixel >>> 24));
        }
        finally { foreach (var image in images) image.Dispose(); }
    });

    private static RenderTargetBitmap Render(Control visual)
    {
        visual.Measure(new Size(Width, Height));
        visual.Arrange(new Rect(0, 0, Width, Height));
        var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        bitmap.Render(visual);
        return bitmap;
    }

    private static int[] Pixels(Bitmap bitmap)
    {
        var pixels = new int[Width * Height];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, Width, Height), pinned.AddrOfPinnedObject(), pixels.Length * 4, Width * 4); }
        finally { pinned.Free(); }
        return pixels;
    }
}
