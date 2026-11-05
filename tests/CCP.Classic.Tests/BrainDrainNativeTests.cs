using Avalonia;
using CCP.Classic.App.Effects;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BrainDrainNativeTests
{
    [Fact]
    public void CaptureUsesBoundedReusableNativeMemoryWithoutSavingDesktopContent()
    {
        using var capture = new BrainDrainDesktopCapture();
        var screen = new PixelRect(0, 0, 256, 256);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => capture.Capture(screen, false)); return;
        }
        var first = capture.Capture(screen, false) ?? throw new InvalidOperationException("Native desktop capture failed");
        var next = capture.Capture(screen, false) ?? throw new InvalidOperationException("Native desktop capture reuse failed");
        Assert.Equal(first.Pixels, next.Pixels); Assert.NotEqual(IntPtr.Zero, first.Pixels);
        Assert.Equal(64, first.Width); Assert.Equal(64, first.Height); Assert.Equal(256, first.Stride);
        capture.SetScreens([]);
        var small = capture.Capture(screen, true) ?? throw new InvalidOperationException("Native reduced capture failed");
        Assert.Equal(32, small.Width); Assert.Equal(32, small.Height);
        capture.SetScreens([]);
    }
}
