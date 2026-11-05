using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class VisualPolishTests
{
    [Fact]
    public async Task ShownMainWindowPresentsDifferentLogoFramesAfterLosingFocus()
    {
        MainWindow? window = null; AnimatedLogo? logo = null; SKColor[]? first = null;
        try
        {
            await UiDispatcher.Run(() =>
            {
                window = new(windows: true); window.Show();
                typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                logo = window.GetVisualDescendants().OfType<AnimatedLogo>().Single();
                Assert.False(window.IsActive); Assert.True(window.IsVisible); Assert.True(logo.IsMotionActive); Assert.NotNull(window.Icon);
            });
            await WaitFor(() => logo!.PresentedFrames >= 3); await Task.Delay(280, TestContext.Current.CancellationToken);
            long frames = 0;
            await UiDispatcher.Run(() => { first = Capture(window!, "studio-inactive-01"); frames = logo!.PresentedFrames; });
            await Task.Delay(450, TestContext.Current.CancellationToken); await WaitFor(() => logo!.PresentedFrames > frames);
            await UiDispatcher.Run(() =>
            {
                var second = Capture(window!, "studio-inactive-02");
                Assert.True(first!.Zip(second).Count(pair => pair.First != pair.Second) > 200);
                window!.Settings.ReducedMotion = true; VisualPolish.ApplyWindow(window, window.Settings);
                Assert.False(logo!.IsMotionActive); frames = logo.PresentedFrames;
            });
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.Equal(frames, logo!.PresentedFrames); Capture(window!, "studio-motion-off"); });
        }
        finally { if (window is not null) await UiDispatcher.Run(window.Close); }
    }

    [Fact]
    public Task OwnedEditorsReceiveTheIconAndMotionPreferencesWithoutFocusDependency() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings(); var owner = new MainWindow(settings);
        try
        {
            owner.Show(); owner.OpenSheet("Settings"); var child = owner.SettingsWindow!;
            Assert.NotNull(owner.Icon); Assert.NotNull(child.Icon); Assert.Contains("motion", child.Classes);
            settings.GentleVisuals = true; VisualPolish.ApplyWindow(owner, settings);
            Assert.DoesNotContain("motion", owner.Classes); Assert.DoesNotContain("motion", child.Classes);
            settings.GentleVisuals = false; settings.PerformanceMode = true; VisualPolish.ApplyWindow(owner, settings);
            Assert.DoesNotContain("motion", child.Classes);
        }
        finally { owner.Close(); }
    });

    private static async Task WaitFor(Func<bool> ready)
    {
        for (var index = 0; index < 100; index++)
        {
            var done = false; await UiDispatcher.Run(() => done = ready());
            if (done) return;
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Visible logo did not present animated frames.");
    }
    private static SKColor[] Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        using var encoded = new MemoryStream(); frame.Save(encoded, PngBitmapEncoderOptions.Default);
        encoded.Position = 0; using var decoded = SKBitmap.Decode(encoded);
        var directory = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default); }
        return decoded.Pixels;
    }
}
