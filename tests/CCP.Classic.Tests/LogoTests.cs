using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LogoTests
{
    [Fact]
    public Task OriginalArtworkChangesAcrossNativeAnimationPhases() => UiDispatcher.Run(() =>
    {
        using var artwork = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/logo.jpg"));
        using var renderer = new DashboardLogoRenderer(artwork);
        using var bitmap = new SKBitmap(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, 256, 256, 0, .7);
        var first = bitmap.Pixels;
        renderer.Draw(canvas, 256, 256, Math.PI, .7);
        var second = bitmap.Pixels;
        var changed = first.Zip(second).Count(pair => pair.First != pair.Second);
        Assert.True(changed > 100, $"Animation changed only {changed} pixels.");
        Assert.Contains(first, pixel => pixel.Alpha > 0);
        renderer.Draw(canvas, 256, 256, 0, .7);
        Assert.Equal(first, bitmap.Pixels);
    });

    [Theory]
    [InlineData("gentle")]
    [InlineData("reduced")]
    [InlineData("performance")]
    public Task MotionStopsForSafetyPreferencesAndDetachment(string preference) => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings();
        var allowed = true;
        var logo = new AnimatedLogo(() => allowed && !settings.GentleVisuals
            && !settings.ReducedMotion && !settings.PerformanceMode);
        Assert.False(logo.IsMotionActive);
        var host = new Window { Content = logo, Width = 256, Height = 256 };
        try
        {
            host.Show();
            Assert.True(logo.IsMotionActive);
            switch (preference)
            {
                case "gentle": settings.GentleVisuals = true; break;
                case "reduced": settings.ReducedMotion = true; break;
                case "performance": settings.PerformanceMode = true; break;
            }
            Assert.False(logo.IsMotionActive);
            settings.GentleVisuals = settings.ReducedMotion = settings.PerformanceMode = false;
            Assert.True(logo.IsMotionActive);
            allowed = false;
            Assert.False(logo.IsMotionActive);
            allowed = true;
            Assert.True(logo.IsMotionActive);
        }
        finally { host.Close(); }
        Assert.False(logo.IsMotionActive);
    });
}
