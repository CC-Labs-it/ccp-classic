using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class CompanionPlacementTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1040);

    [Fact]
    public void PersistedPlacementUsesTheOriginalScaleBoundsAndRejectsInvalidCoordinates()
    {
        var options = new CompanionPlacementOptions { Scale = double.NaN, Left = int.MaxValue, Top = 10 };
        options.Normalize(); Assert.Equal(1, options.Scale); Assert.Null(options.Left); Assert.Null(options.Top);
        options.Scale = 2; options.Normalize(); Assert.Equal(1.5, options.Scale);
        options.Scale = .1; options.Normalize(); Assert.Equal(.5, options.Scale);
    }

    [Fact]
    public void TheAvatarAndItsBarksAreOnByDefaultAndResetKeepsThat()
    {
        var settings = new ClassicSettings();
        Assert.True(settings.CompanionEnabled); Assert.True(settings.BarksEnabled);
        settings.CompanionEnabled = false; settings.BarksEnabled = false; settings.ResetToDefaults();
        Assert.True(settings.CompanionEnabled); Assert.True(settings.BarksEnabled);
    }

    [Fact]
    public void TheTubeScaleFollowsTheMonitorWithinItsBounds()
    {
        // 1080p: the width share wins (1920 * .30 / 780).
        Assert.Equal(1920 * .30 / 780, TubeLayout.Scale(FullHd, 1), 6);
        // A wide, short monitor: the height share wins.
        Assert.Equal(1000 * .85 / 1020, TubeLayout.Scale(new(0, 0, 4000, 1000), 1), 6);
        // Physical pixels are converted to DIP first.
        Assert.Equal(TubeLayout.Scale(FullHd, 1), TubeLayout.Scale(new(0, 0, 3840, 2080), 2), 6);
        Assert.Equal(.4, TubeLayout.Scale(new(0, 0, 640, 480), 1));
        Assert.Equal(1, TubeLayout.Scale(new(0, 0, 7680, 4320), 1));
        var size = TubeLayout.WindowSize(.5);
        Assert.Equal(TubeLayout.BandWidth * .5, size.Width); Assert.Equal(510, size.Height);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void AttachedTubeDocksItsArtThreeDipLeftOfTheMainWindow(double scaling)
    {
        var work = new PixelRect(0, 0, (int)(1920 * scaling), (int)(1040 * scaling));
        var main = new PixelRect((int)(800 * scaling), (int)(100 * scaling), (int)(724 * scaling), (int)(800 * scaling));
        var scale = TubeLayout.Scale(work, scaling);
        var dock = TubeLayout.Dock(main, work, scaling, scale);
        Assert.Equal(TubeDockSide.Left, dock.Side); Assert.False(dock.Mirrored);
        var unit = scale * scaling;
        var artRight = dock.Position.X + (TubeLayout.ArtRight - TubeLayout.BandLeft) * unit;
        Assert.InRange(main.X - artRight, 3 * scaling - 1, 3 * scaling + 1);
        // The window itself never reaches over the main window.
        Assert.True(dock.Position.X + TubeLayout.BandWidth * unit <= main.X + 1);
        var top = main.Y + (main.Height - TubeLayout.CanvasHeight * unit) / 2 + 20 * unit;
        Assert.InRange(dock.Position.Y, top - 1, top + 1);
    }

    [Fact]
    public void WithNoRoomOnTheLeftTheMirroredTubeDocksOnTheRight()
    {
        var scale = TubeLayout.Scale(FullHd, 1);
        var main = new PixelRect(20, 120, 724, 800);
        var dock = TubeLayout.Dock(main, FullHd, 1, scale);
        Assert.Equal(TubeDockSide.Right, dock.Side); Assert.True(dock.Mirrored);
        var artLeft = dock.Position.X + (TubeLayout.MirroredX(TubeLayout.ArtRight) - TubeLayout.BandLeft) * scale;
        Assert.InRange(artLeft - main.Right, 2, 4);
        Assert.True(dock.Position.X >= main.Right - 1);
    }

    [Fact]
    public void WithNoRoomOnEitherSideTheArtStaysOnScreenOnTheRoomierSide()
    {
        var scale = TubeLayout.Scale(FullHd, 1);
        foreach (var (main, mirrored) in new[] { (new PixelRect(10, 0, 1800, 1040), true), (new PixelRect(110, 0, 1800, 1040), false) })
        {
            var dock = TubeLayout.Dock(main, FullHd, 1, scale);
            Assert.Equal(TubeDockSide.Float, dock.Side); Assert.Equal(mirrored, dock.Mirrored);
            var artLeft = mirrored ? TubeLayout.MirroredX(TubeLayout.ArtRight) : TubeLayout.ArtLeft;
            var artRight = mirrored ? TubeLayout.MirroredX(TubeLayout.ArtLeft) : TubeLayout.ArtRight;
            Assert.True(dock.Position.X + (artLeft - TubeLayout.BandLeft) * scale >= FullHd.X - 1);
            Assert.True(dock.Position.X + (artRight - TubeLayout.BandLeft) * scale <= FullHd.Right + 1);
        }
    }

    [Fact]
    public void VerticalDockClampsIntoTheWorkAreaOrCentresWhenTaller()
    {
        var scale = TubeLayout.Scale(FullHd, 1); var height = TubeLayout.CanvasHeight * scale;
        var low = TubeLayout.Dock(new(900, 900, 724, 800), FullHd, 1, scale);
        Assert.InRange(low.Position.Y + height, FullHd.Bottom - 1, FullHd.Bottom + 1);
        var high = TubeLayout.Dock(new(900, -700, 724, 800), FullHd, 1, scale);
        Assert.Equal(FullHd.Y, high.Position.Y);
        var shortArea = new PixelRect(0, 100, 1920, 300);
        var centred = TubeLayout.Dock(new(900, 0, 724, 800), shortArea, 1, 1);
        Assert.InRange(centred.Position.Y + TubeLayout.CanvasHeight / 2, shortArea.Y + 149, shortArea.Y + 151);
    }

    [Fact]
    public Task DetachedScaleAndTopmostApplyWithoutAPlaybackOrDesktopChange() => UiDispatcher.Run(() =>
    {
        var owner = new Window(); var settings = new ClassicSettings();
        var companion = new CompanionWindow(owner, settings, _ => { }, requireTransparency: false);
        try
        {
            var apply = typeof(CompanionWindow).GetMethod("ApplyPlacement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(companion, null);
            var attachedWidth = companion.Width; var attachedHeight = companion.Height;
            Assert.Equal(TubeLayout.CanvasHeight / TubeLayout.BandWidth, attachedHeight / attachedWidth, 6);
            settings.CompanionPlacement.Detached = true; settings.CompanionPlacement.Scale = 1.5;
            settings.CompanionPlacement.AlwaysOnTop = false; apply.Invoke(companion, null);
            Assert.Equal(attachedWidth * 1.5, companion.Width, 6); Assert.Equal(attachedHeight * 1.5, companion.Height, 6);
            Assert.False(companion.Topmost); Assert.False(companion.IsMirrored);
            settings.CompanionPlacement.Detached = false; apply.Invoke(companion, null);
            Assert.Equal(attachedWidth, companion.Width, 6); Assert.Equal(attachedHeight, companion.Height, 6);
            Assert.False(companion.IsVisible);
            // A docked tube is never topmost, even with Always on top set; detached it is.
            settings.CompanionPlacement.AlwaysOnTop = true; apply.Invoke(companion, null); Assert.False(companion.Topmost);
            settings.CompanionPlacement.Detached = true; apply.Invoke(companion, null); Assert.True(companion.Topmost);
        }
        finally { companion.FinishImmediately(); owner.Close(); }
    });
}
