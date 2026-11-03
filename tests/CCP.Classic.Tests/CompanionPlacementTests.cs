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
    public void TheAvatarIsOnByDefaultWithBarksOffAndResetKeepsThat()
    {
        var settings = new ClassicSettings();
        Assert.True(settings.CompanionEnabled); Assert.False(settings.BarksEnabled);
        settings.CompanionEnabled = false; settings.BarksEnabled = true; settings.ResetToDefaults();
        Assert.True(settings.CompanionEnabled); Assert.False(settings.BarksEnabled);
    }

    [Fact]
    public void TheTubeScaleFollowsThePanelWithinItsBounds()
    {
        // The owner's panel: 800 DIP visible, so the painted art is 38% of it (about 304 DIP).
        var scale = TubeLayout.Scale(800, 1104);
        Assert.Equal(800 * TubeLayout.ArtToPanelHeight / TubeLayout.ArtHeight, scale, 6);
        Assert.InRange(scale, .55, .57);
        Assert.InRange(TubeLayout.ArtHeight * scale, 303, 305);
        Assert.InRange(TubeLayout.WindowSize(scale).Height, 570, 573);
        // Clamp bounds before the player's size, and an unknown panel uses the largest scale.
        Assert.Equal(TubeLayout.MinScale, TubeLayout.Scale(200, 1104));
        Assert.Equal(TubeLayout.MaxScale, TubeLayout.Scale(3000, 4000));
        Assert.Equal(TubeLayout.MaxScale, TubeLayout.Scale(0, 0));
        // Companion size multiplies the panel scale, within its own .5 to 1.5 bounds.
        Assert.Equal(scale * 1.5, TubeLayout.Scale(800, 1104, 1.5), 6);
        Assert.Equal(scale * .5, TubeLayout.Scale(800, 1104, .25), 6);
        Assert.Equal(scale, TubeLayout.Scale(800, 1104, double.NaN), 6);
        // The whole window never outgrows the work area.
        Assert.Equal(700 / TubeLayout.CanvasHeight, TubeLayout.Scale(800, 700, 1.5), 6);
        var size = TubeLayout.WindowSize(.5);
        Assert.Equal(TubeLayout.BandWidth * .5, size.Width); Assert.Equal(510, size.Height);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void AttachedTubeArtMeetsTheVisibleLeftEdgeOfTheMainWindow(double scaling)
    {
        var work = new PixelRect(0, 0, (int)(1920 * scaling), (int)(1040 * scaling));
        var main = new PixelRect((int)(800 * scaling), (int)(100 * scaling), (int)(724 * scaling), (int)(800 * scaling));
        var scale = TubeLayout.Scale(800, 1040);
        var dock = TubeLayout.Dock(main, work, scaling, scale);
        Assert.Equal(TubeDockSide.Left, dock.Side); Assert.False(dock.Mirrored);
        var unit = scale * scaling;
        // No daylight: the pipes reach main's visible edge and overlap it by at most a DIP.
        var artRight = dock.Position.X + (TubeLayout.ArtRight - TubeLayout.BandLeft) * unit;
        Assert.InRange(artRight - main.X, 0, TubeLayout.DockOverlap * scaling + 1);
        Assert.True(dock.Position.X + TubeLayout.BandWidth * unit <= main.X + TubeLayout.DockOverlap * scaling + 1);
        var top = main.Y + (main.Height - TubeLayout.CanvasHeight * unit) / 2 + 20 * unit;
        Assert.InRange(dock.Position.Y, top - 1, top + 1);
    }

    [Fact]
    public void DockingUsesTheVisibleFrameWithoutInvisibleBorders()
    {
        // Windows 10/11: the outer frame carries 7 px invisible borders left, right and bottom.
        var outer = new PixelRect(793, 100, 738, 807);
        var visible = new PixelRect(800, 100, 724, 800);
        Assert.Equal(visible, TubeLayout.VisibleFrame(outer, visible));
        Assert.Equal(outer, TubeLayout.VisibleFrame(outer, null));
        Assert.Equal(outer, TubeLayout.VisibleFrame(outer, new PixelRect(0, 0, 0, 0)));
        Assert.Equal(outer, TubeLayout.VisibleFrame(outer, new PixelRect(-32000, -32000, 160, 28)));
        var scale = TubeLayout.Scale(800, 1040);
        var dock = TubeLayout.Dock(TubeLayout.VisibleFrame(outer, visible), FullHd, 1, scale);
        var artRight = dock.Position.X + (TubeLayout.ArtRight - TubeLayout.BandLeft) * scale;
        Assert.InRange(artRight - visible.X, 0, TubeLayout.DockOverlap + 1);
    }

    [Fact]
    public void WithNoRoomOnTheLeftTheMirroredTubeDocksOnTheRight()
    {
        var scale = TubeLayout.Scale(800, 1040);
        var main = new PixelRect(20, 120, 724, 800);
        var dock = TubeLayout.Dock(main, FullHd, 1, scale);
        Assert.Equal(TubeDockSide.Right, dock.Side); Assert.True(dock.Mirrored);
        var artLeft = dock.Position.X + (TubeLayout.MirroredX(TubeLayout.ArtRight) - TubeLayout.BandLeft) * scale;
        Assert.InRange(main.Right - artLeft, 0, TubeLayout.DockOverlap + 1);
        Assert.True(dock.Position.X >= main.Right - TubeLayout.DockOverlap - 1);
    }

    [Fact]
    public void WithNoRoomOnEitherSideTheArtStaysOnScreenOnTheRoomierSide()
    {
        var scale = TubeLayout.Scale(1040, 1040);
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
        var scale = TubeLayout.Scale(800, 1040); var height = TubeLayout.CanvasHeight * scale;
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
            // Companion size applies to the docked tube too.
            settings.CompanionPlacement.Detached = false; apply.Invoke(companion, null);
            Assert.Equal(attachedWidth * 1.5, companion.Width, 6); Assert.Equal(attachedHeight * 1.5, companion.Height, 6);
            settings.CompanionPlacement.Scale = 1; apply.Invoke(companion, null);
            Assert.Equal(attachedWidth, companion.Width, 6); Assert.Equal(attachedHeight, companion.Height, 6);
            Assert.False(companion.IsVisible);
            // Ctrl+wheel resizes docked and saves only the size, never a detached position.
            var saved = 0; companion.PlacementChanged += () => saved++;
            companion.ResizeBy(-.25);
            Assert.Equal(.75, settings.CompanionPlacement.Scale); Assert.Equal(1, saved);
            Assert.Equal(attachedHeight * .75, companion.Height, 6);
            Assert.Null(settings.CompanionPlacement.Left); Assert.Null(settings.CompanionPlacement.Top);
            companion.ResizeBy(-1); Assert.Equal(.5, settings.CompanionPlacement.Scale);
            settings.CompanionPlacement.Scale = 1; apply.Invoke(companion, null);
            // A docked tube is never topmost, even with Always on top set; detached it is.
            settings.CompanionPlacement.AlwaysOnTop = true; apply.Invoke(companion, null); Assert.False(companion.Topmost);
            settings.CompanionPlacement.Detached = true; apply.Invoke(companion, null); Assert.True(companion.Topmost);
        }
        finally { companion.FinishImmediately(); owner.Close(); }
    });
}
