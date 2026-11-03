using Avalonia.Controls;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LoomRendererTests
{
    [Theory]
    [InlineData("log")]
    [InlineData("arch")]
    [InlineData("ribbon")]
    [InlineData("golden")]
    [InlineData("tunnel")]
    [InlineData("petal")]
    public Task EveryNativeStyleMovesAndRepeatsWithoutASeam(string style) => UiDispatcher.Run(() =>
    {
        var q = new LoomDefinition { Layer = new() { Style = style, Arms = 3, Colors = ["#ff69b4", "#ffffff"] } };
        using var first = LoomRenderer.Render(q, 96, 0);
        using var moved = LoomRenderer.Render(q, 96, .23);
        using var looped = LoomRenderer.Render(q, 96, 1);
        Assert.Equal(first.Pixels, looped.Pixels);
        Assert.True(first.Pixels.Zip(moved.Pixels).Count(pair => pair.First != pair.Second) > 100);
        Assert.All(first.Pixels, color => Assert.Equal(255, color.Alpha));
    });

    [Fact]
    public Task NativeLayerAndColorKnobsChangeTheActualPixels() => UiDispatcher.Run(() =>
    {
        var q = new LoomDefinition { Layer = new() { Colors = ["#ff69b4", "#ffffff"] } };
        using var first = LoomRenderer.Render(q, 96, .23);
        var variants = new[]
        {
            q with { Layer = q.Layer with { BandMode = "gradient" } },
            q with { Layer2 = q.Layer2 with { Enabled = true } },
            q with { Bg = new("radial", "#14060f", "#ffffff") },
            q with { Pulse = new(.2, 1) }, q with { Wobble = new(.2, 4, 1) },
            q with { Glow = .7 }, q with { HueCycles = 1 }
        };
        foreach (var variant in variants)
        {
            using var changed = LoomRenderer.Render(variant, 96, .23);
            Assert.True(first.Pixels.Zip(changed.Pixels).Count(pair => pair.First != pair.Second) > 100);
        }
        using var framed = LoomRenderer.Render(q with { Centerpiece = new("dot", "#000000", .3) }, 96, .23);
        Assert.Equal(SKColors.Black, framed.GetPixel(48, 48));
    });

    [Fact]
    public Task RasterBudgetAndGentlePreferencesAreAppliedWithoutChangingTheDefinition() => UiDispatcher.Run(() =>
    {
        var q = new LoomDefinition { Format = "wide", Pulse = new(.2, 3), Wobble = new(.3, 4, 2), Layer2 = new() { Enabled = true } };
        using var normal = LoomRenderer.Render(q, 2000, .23);
        using var cheap = LoomRenderer.Render(q, 2000, .23, performance: true);
        using var bright = LoomRenderer.Render(q, 96, .23);
        using var gentle = LoomRenderer.Render(q, 96, .23, gentle: true);
        Assert.Equal((512, 288), (normal.Width, normal.Height));
        Assert.Equal((320, 180), (cheap.Width, cheap.Height));
        Assert.NotEqual(bright.Pixels, gentle.Pixels);
        Assert.True(q.Layer2.Enabled); Assert.Equal(.2, q.Pulse.Amp); Assert.Equal(.3, q.Wobble.Amp);
    });

    [Fact]
    public async Task ReducedMotionHoldsTheDisplayedPoseAndHiddenOrDetachedViewsStopRendering()
    {
        var settings = new ClassicSettings(); LoomPreview? preview = null; Window? window = null;
        try
        {
            await UiDispatcher.Run(() =>
            {
                preview = new(settings) { Width = 160, Height = 160, MotionAllowed = () => true };
                window = new Window { Width = 180, Height = 180, Content = preview }; window.Show();
            });
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var ready = false; await UiDispatcher.Run(() => ready = preview!.HasFrame);
                if (ready) break;
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await UiDispatcher.Run(() => { Assert.True(preview!.HasFrame, preview.LastError); settings.ReducedMotion = true; });
            await Task.Delay(100, TestContext.Current.CancellationToken);
            double phase = 0; long count = 0;
            await UiDispatcher.Run(() => { phase = preview!.Phase; count = preview.RenderedFrames; Assert.False(preview.IsMotionActive); });
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.Equal(phase, preview!.Phase); Assert.Equal(count, preview.RenderedFrames); window!.Hide(); settings.ReducedMotion = false; });
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.Equal(count, preview!.RenderedFrames); window!.Content = null; Assert.False(preview.IsMotionActive); });
        }
        finally { await UiDispatcher.Run(() => window?.Close()); }
    }
}
