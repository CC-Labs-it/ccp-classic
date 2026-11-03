using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class InAppEffectWindowTests
{
    private static readonly EffectFrame Pink = new() { PinkOpacity = .2 };

    [Fact]
    public Task FallbackIsAnOpaqueOwnedNormalWindowAndEmptyFramesNeverShowIt() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Window.UpdateFrame(EffectFrame.Empty); Assert.False(fixture.Window.IsVisible);
        fixture.Window.UpdateFrame(new() { PinkOpacity = double.NaN, SpiralOpacity = -1, SubliminalText = "Focus" });
        Assert.False(fixture.Window.IsVisible);
        fixture.Frame(Pink);
        Assert.True(fixture.Window.IsVisible); Assert.Same(fixture.Owner, fixture.Window.Owner);
        Assert.False(fixture.Window.Topmost); Assert.Equal(WindowState.Normal, fixture.Window.WindowState);
        Assert.Equal(WindowDecorations.Full, fixture.Window.WindowDecorations);
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(fixture.Window.Background).Color.A);
        Assert.Equal(640, fixture.Window.Width); Assert.Equal(480, fixture.Window.Height);
        Assert.Equal("Studio", fixture.Window.Title); Assert.Equal(1, fixture.Configured);
        fixture.Frame(EffectFrame.Empty); Assert.False(fixture.Window.IsVisible);
        Assert.Equal(EffectFrame.Empty, fixture.Window.View.Frame); Assert.False(fixture.Window.HasFinished);
        fixture.Frame(Pink); Assert.True(fixture.Window.IsVisible);
    });

    [Fact]
    public Task UserCloseConsultsTheSamePanicGateAndForcedStopClearsEveryFrame() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(Pink);
        fixture.Session.SetPanicEnabled(false, true);
        fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Session.IsRunning); Assert.True(fixture.Window.IsVisible); Assert.Equal(1, fixture.StopRequests);
        fixture.Session.SetPanicEnabled(true);
        fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.False(fixture.Session.IsRunning); Assert.False(fixture.Window.IsVisible);
        Assert.True(fixture.Window.HasFinished); Assert.Equal(EffectFrame.Empty, fixture.Window.View.Frame);
        fixture.Window.UpdateFrame(Pink); Assert.False(fixture.Window.IsVisible);
    });

    [Fact]
    public Task ParentCloseAndDisposalAlwaysReleaseEvenWithPanicDisabled() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Session.SetPanicEnabled(false, true); fixture.Frame(Pink);
        fixture.Owner.Close(); Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.HasFinished); Assert.False(fixture.Window.IsVisible);
        Assert.Equal(EffectFrame.Empty, fixture.Window.View.Frame); Assert.Equal(0, fixture.StopRequests);
        fixture.Window.Dispose(); fixture.Window.FinishImmediately();
    });

    [Fact]
    public Task AQueuedCloseCannotStopANewSessionAfterItsWindowIsFinished() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(Pink);
        fixture.Window.Close(); fixture.Window.FinishImmediately(); fixture.Session.Start();
        Dispatcher.UIThread.RunJobs(); Assert.True(fixture.Session.IsRunning); Assert.Equal(0, fixture.StopRequests);
    });

    [Fact]
    public Task MinimizingOrHidingTheOwnerClearsTheViewAndRestoresOnlyCurrentVisibleContent() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(Pink);
        fixture.Owner.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
        Assert.False(fixture.Window.IsVisible); Assert.Equal(EffectFrame.Empty, fixture.Window.View.Frame);
        fixture.Owner.WindowState = WindowState.Normal; Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.IsVisible); Assert.Equal(Pink, fixture.Window.View.Frame);
        fixture.Owner.Hide(); Dispatcher.UIThread.RunJobs(); Assert.False(fixture.Window.IsVisible);
        fixture.Window.UpdateFrame(EffectFrame.Empty); fixture.Owner.Show(); Dispatcher.UIThread.RunJobs();
        Assert.False(fixture.Window.IsVisible);
    });

    [Fact]
    public Task PointerInputUsesTheRenderedViewportAndOnlyLeftClicks() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(Pink);
        var events = new List<(Point Point, Size Size)>();
        fixture.Window.InputPressed += (point, size) => events.Add((point, size));
        var viewport = fixture.Window.View.Bounds.Size;
        fixture.Window.MouseDown(new Point(viewport.Width * .25, viewport.Height * .75), MouseButton.Left);
        fixture.Window.MouseUp(new Point(viewport.Width * .25, viewport.Height * .75), MouseButton.Left);
        fixture.Window.MouseDown(new Point(20, 20), MouseButton.Right); fixture.Window.MouseUp(new Point(20, 20), MouseButton.Right);
        var hit = Assert.Single(events);
        Assert.Equal(.25, hit.Point.X, 8); Assert.Equal(.75, hit.Point.Y, 8); Assert.Equal(viewport, hit.Size);
        fixture.Window.FinishImmediately(); Assert.Equal(EffectFrame.Empty, fixture.Window.View.Frame);
    });

    [Fact]
    public Task VisibilityIncludesInteractiveCuesAndCallerRetainsItsImages() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture();
        using var image = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var frames = new EffectFrame[]
        {
            new() { Flashes = [new(image, new Rect(.2, .2, .2, .2), .5)] },
            new() { SubliminalText = "Focus", SubliminalOpacity = .5 },
            new() { Interactive = new([new(1, .2, .2, .1, .5, true)], null) },
            new() { Interactive = new([], new("Focus", .2, .2, .3, .5)) },
            new() { BrainDrain = new(.3, .5, .2, false) }
        };
        foreach (var frame in frames) { fixture.Frame(frame); Assert.True(fixture.Window.IsVisible); }
        fixture.Window.FinishImmediately(); using var stillUsable = image.Lock(); Assert.NotEqual(IntPtr.Zero, stillUsable.Address);
    });

    private sealed class Fixture : IDisposable
    {
        public Window Owner { get; } = new() { Width = 724, Height = 760 };
        public SessionController Session { get; } = new(new());
        public InAppEffectWindow Window { get; }
        public int Configured, StopRequests;
        public Fixture()
        {
            Owner.Show(); Session.Start();
            Window = new(Owner, new(), _ => Configured++, () => { StopRequests++; return Session.Stop(); });
            Session.StopRequested += Window.FinishImmediately;
        }
        public void Frame(EffectFrame frame) { Window.UpdateFrame(frame); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { Window.FinishImmediately(); Owner.Close(); Session.Dispose(); Dispatcher.UIThread.RunJobs(); }
    }
}
