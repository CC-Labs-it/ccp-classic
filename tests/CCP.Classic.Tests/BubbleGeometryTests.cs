using Avalonia;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BubbleGeometryTests
{
    private static BubbleCue Bubble(double x = .25, double y = .25, double size = .1) => new(1, x, y, size, 1, true);

    [Theory]
    [InlineData(1920, 1080, 108)]
    [InlineData(1080, 1920, 108)]
    [InlineData(3840, 2160, 216)]
    [InlineData(1000, 1000, 100)]
    public void DrawingAndInputShareTheSamePhysicalCircle(double width, double height, double diameter)
    {
        var bubble = Bubble(); var bounds = BubbleGeometry.NormalizedBounds(bubble, width, height);
        Assert.Equal(diameter, bounds.Width * width, 8); Assert.Equal(diameter, bounds.Height * height, 8);
        var pixels = Assert.Single(NativeOverlayInput.PixelBounds([bounds], new((int)width, (int)height)));
        Assert.InRange(pixels.Width, diameter, diameter + 1); Assert.InRange(pixels.Height, diameter, diameter + 1);
        Assert.True(BubbleGeometry.HitTest(bubble, bounds.Center, width, height));
        Assert.True(NativeOverlayInput.Hits([pixels], (int)(bounds.Center.X * width), (int)(bounds.Center.Y * height)));
    }

    [Fact]
    public void LandscapeBubblesDoNotClaimTheOldInvisibleStrip()
    {
        var bubble = Bubble(); var bounds = BubbleGeometry.NormalizedBounds(bubble, 1920, 1080);
        Assert.Equal(.05625, bounds.Width, 8); Assert.Equal(.1, bounds.Height, 8);
        var invisible = new Point(bubble.X + .09, bubble.Y + .05);
        Assert.False(BubbleGeometry.HitTest(bubble, invisible, 1920, 1080));
        Assert.False(NativeOverlayInput.Hits(NativeOverlayInput.PixelBounds([bounds], new(1920, 1080)),
            (int)(invisible.X * 1920), (int)(invisible.Y * 1080)));
        Assert.False(BubbleGeometry.HitTest(bubble, new(bounds.X + .001, bounds.Y + .001), 1920, 1080));
    }

    [Fact]
    public void PortraitAndScaledViewsPreserveNormalizedGeometryAndRejectBoundingCorners()
    {
        var bubble = Bubble(); var first = BubbleGeometry.NormalizedBounds(bubble, 1000, 2000);
        Assert.Equal(first, BubbleGeometry.NormalizedBounds(bubble, 2000, 4000));
        Assert.False(BubbleGeometry.HitTest(bubble, new(first.Right - .001, first.Bottom - .001), 1000, 2000));
        Assert.False(BubbleGeometry.HitTest(bubble, new(bubble.X + .05, bubble.Y + .09), 1000, 2000));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(double.NaN, 1000)]
    [InlineData(1000, double.PositiveInfinity)]
    public void InvalidViewportIsEmptyAndCannotBeClicked(double width, double height)
    {
        Assert.Equal(default, BubbleGeometry.NormalizedBounds(Bubble(), width, height));
        Assert.False(BubbleGeometry.HitTest(Bubble(), new(.3, .3), width, height));
    }

    [Fact]
    public void InvalidBubblesAndPointsAreRejectedAndPartialBubblesClipSafely()
    {
        foreach (var bubble in new[] { Bubble(size: double.NaN), Bubble(size: -1), Bubble(x: double.PositiveInfinity), Bubble(y: 2) })
        { Assert.Equal(default, BubbleGeometry.NormalizedBounds(bubble, 1000, 1000)); Assert.False(BubbleGeometry.HitTest(bubble, new(.3, .3), 1000, 1000)); }
        Assert.False(BubbleGeometry.HitTest(Bubble(), new(double.NaN, .3), 1000, 1000));
        Assert.False(BubbleGeometry.HitTest(Bubble(), new(-.1, .3), 1000, 1000));
        var partial = Bubble(x: -.05);
        var bounds = BubbleGeometry.NormalizedBounds(partial, 1000, 1000);
        Assert.Equal(0, Assert.Single(NativeOverlayInput.PixelBounds([bounds], new(1000, 1000))).X);
        Assert.True(BubbleGeometry.HitTest(partial, new(.01, .3), 1000, 1000));
    }
}
