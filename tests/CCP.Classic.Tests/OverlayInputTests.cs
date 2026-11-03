using Avalonia;
using CCP.Classic.App.Effects;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class OverlayInputTests
{
    [Theory]
    [InlineData(":0", true)]
    [InlineData(":12.1", true)]
    [InlineData("unix:0.0", true)]
    [InlineData("localhost:10.0", false)]
    [InlineData("192.0.2.1:0", false)]
    [InlineData("host:0", false)]
    [InlineData("unix/host:0", false)]
    [InlineData(":0.1.2", false)]
    [InlineData(":.1", false)]
    [InlineData(":0.", false)]
    [InlineData(null, false)]
    public void OnlyLocalDisplayAddressesAreAccepted(string? display, bool accepted) =>
        Assert.Equal(accepted, NativeOverlayInput.IsLocalDisplay(display));

    [Fact]
    public void InputRegionsClipToTheMonitorAndUsePhysicalPixels()
    {
        var rectangles = NativeOverlayInput.PixelBounds(
            [new Rect(.25, .25, .5, .5), new Rect(-.1, .8, .3, .4), new Rect(2, 2, .1, .1)], new PixelSize(1920, 1080));
        Assert.Equal(2, rectangles.Length);
        Assert.Equal(new PixelRect(480, 270, 960, 540), rectangles[0]);
        Assert.Equal(0, rectangles[1].X);
        Assert.Equal(1080, rectangles[1].Bottom);
        Assert.True(NativeOverlayInput.Hits(rectangles, 480, 270));
        Assert.False(NativeOverlayInput.Hits(rectangles, 1440, 810));
        Assert.False(NativeOverlayInput.Hits(rectangles, 10, 10));
        Assert.Empty(NativeOverlayInput.PixelBounds([], new PixelSize(1920, 1080)));
    }

    [Fact]
    public void WindowsInputStylePreservesNoActivateAcrossClickabilityChanges()
    {
        var passive = NativeOverlayInput.WindowsStyle(0x100, true);
        var interactive = NativeOverlayInput.WindowsStyle(passive, false);
        Assert.NotEqual(0u, passive & 0x20);
        Assert.Equal(0u, interactive & 0x20);
        Assert.NotEqual(0u, interactive & 0x08000000);
        Assert.NotEqual(0u, interactive & 0x80000);
        Assert.NotEqual(0u, interactive & 0x80);
        Assert.NotEqual(0u, interactive & 0x100);
    }
}
