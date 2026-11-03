using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Effects;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class VisualEffectsTests
{
    [Fact]
    public Task EmptyAndZeroOpacityFramesStayTransparent() => UiDispatcher.Run(() =>
    {
        var view = View();
        Assert.All(Pixels(view), pixel => Assert.Equal(0, pixel >>> 24));
        view.UpdateFrame(new EffectFrame { PinkOpacity = 0, SpiralOpacity = 0, SubliminalText = "Test" });
        Assert.All(Pixels(view), pixel => Assert.Equal(0, pixel >>> 24));
    });

    [Fact]
    public Task PinkTintHonoursFinalOpacityAndClampsInvalidInput() => UiDispatcher.Run(() =>
    {
        var view = View();
        view.UpdateFrame(new EffectFrame { PinkOpacity = .5 });
        var pixel = Pixels(view)[120 * 320 + 160];
        Assert.InRange(pixel >>> 24, 126, 129);
        Assert.True(((pixel >>> 16) & 255) > ((pixel >>> 8) & 255));
        view.UpdateFrame(new EffectFrame { PinkOpacity = double.NaN, SpiralOpacity = -1 });
        Assert.All(Pixels(view), value => Assert.Equal(0, value >>> 24));
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task NativeSpiralChoicesAnimateAndKeepFrozenPhaseStable(int choice) => UiDispatcher.Run(() =>
    {
        var view = View();
        var frame = new EffectFrame { SpiralKind = choice, SpiralOpacity = .4, SpiralPhase = .2 };
        view.UpdateFrame(frame);
        var first = Pixels(view);
        Assert.True(first.Count(pixel => pixel >>> 24 > 0) > 1000);
        view.UpdateFrame(frame with { SpiralPhase = .8 });
        var moved = Pixels(view);
        Assert.True(first.Zip(moved).Count(pair => pair.First != pair.Second) > 1000);
        view.UpdateFrame(frame with { SpiralPhase = .8, ReducedMotion = true });
        Assert.Equal(moved, Pixels(view));
    });

    [Fact]
    public Task FlashDrawsOnlyInsideItsNormalizedBoundsAndCallerKeepsImage() => UiDispatcher.Run(() =>
    {
        var view = View();
        using var image = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = image.Lock())
        {
            var row = Enumerable.Repeat(unchecked((int)0xFFFF0000), 8).ToArray();
            for (var y = 0; y < 8; y++) Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, row.Length);
        }
        view.UpdateFrame(new EffectFrame { FlashImage = image, FlashOpacity = 1, FlashBounds = new Rect(.25, .25, .5, .5) });
        var pixels = Pixels(view);
        Assert.Equal(0xFFFF0000u, (uint)pixels[120 * 320 + 160]);
        Assert.Equal(0, pixels[20 * 320 + 20]);
        Assert.Equal(0, pixels[60 * 320 + 80]);
        view.UpdateFrame(new EffectFrame { Flashes =
            [new(image, new Rect(.1, .1, .2, .2), 1), new(image, new Rect(.7, .7, .2, .2), 1)] });
        pixels = Pixels(view);
        Assert.Equal(0xFFFF0000u, (uint)pixels[48 * 320 + 64]);
        Assert.Equal(0xFFFF0000u, (uint)pixels[192 * 320 + 256]);
        Assert.Equal(0, pixels[120 * 320 + 160]);
        view.UpdateFrame(EffectFrame.Empty);
        using var stillUsable = image.Lock();
        Assert.NotEqual(IntPtr.Zero, stillUsable.Address);
        Assert.All(Pixels(view), pixel => Assert.Equal(0, pixel >>> 24));
    });

    [Theory]
    [InlineData("Test")]
    [InlineData("放松")]
    [InlineData("Отдых")]
    public Task SubliminalTextUsesNativeShapingAndFinalOpacity(string text) => UiDispatcher.Run(() =>
    {
        var view = View();
        view.UpdateFrame(new EffectFrame { SubliminalText = text, SubliminalOpacity = .4, SubliminalColour = Colors.White });
        var pixels = Pixels(view);
        Assert.True(pixels.Count(pixel => pixel >>> 24 > 0) > 100);
        Assert.InRange(pixels.Max(pixel => pixel >>> 24), 100, 104);
    });

    private static VisualEffectsControl View()
    {
        var view = new VisualEffectsControl { Width = 320, Height = 240 };
        view.Measure(new Size(320, 240));
        view.Arrange(new Rect(0, 0, 320, 240));
        return view;
    }

    private static int[] Pixels(VisualEffectsControl view)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 240), new Vector(96, 96));
        bitmap.Render(view);
        var pixels = new int[320 * 240];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 320, 240), pinned.AddrOfPinnedObject(), pixels.Length * 4, 320 * 4); }
        finally { pinned.Free(); }
        return pixels;
    }
}
