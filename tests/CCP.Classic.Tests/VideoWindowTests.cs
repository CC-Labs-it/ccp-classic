using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class VideoWindowTests
{
    [Fact]
    public Task StrictCloseIsBlockedAndForcedStopClearsFrameBeforeDisposal() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(strict: true);
        fixture.Frame(0); fixture.Window.Pulse(.25, 1);
        fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.IsVisible); Assert.True(fixture.Session.IsRunning);
        fixture.Playback.Disposing = () => Assert.Null(fixture.Image.Source);
        fixture.Window.FinishImmediately(); fixture.Window.FinishImmediately();
        Assert.Null(fixture.Image.Source); Assert.False(fixture.Window.IsVisible);
        Assert.True(fixture.Window.HasFinished); Assert.Equal(1, fixture.Playback.Disposals);
    });

    [Fact]
    public Task NonStrictCloseConsultsTheSharedPanicGate() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0);
        fixture.Session.SetPanicEnabled(false, true);
        fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.IsVisible); Assert.True(fixture.Session.IsRunning);
        fixture.Session.SetPanicEnabled(true);
        fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.False(fixture.Session.IsRunning); Assert.False(fixture.Window.IsVisible);
        Assert.Equal(1, fixture.Playback.Disposals);
    });

    [Fact]
    public Task AQueuedCloseCannotStopAFreshSessionAfterTheOldWindowFinishes() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0);
        fixture.Window.Close(); fixture.Window.FinishImmediately(); fixture.Session.Start();
        Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Session.IsRunning);
    });

    [Fact]
    public Task NoFramePlayersRemainHiddenAndWatchdogStartsAtTheirFirstPulse() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture();
        Assert.False(fixture.Window.IsVisible); Assert.Equal(1, fixture.Playback.Starts);
        fixture.Window.Pulse(1000, 1); fixture.Window.Pulse(1014.9, 1);
        Assert.False(fixture.Window.IsVisible); Assert.False(fixture.Window.HasFinished);
        fixture.Window.Pulse(1015, 1);
        Assert.True(fixture.Window.HasFinished); Assert.NotNull(fixture.Window.PlaybackError);
        Assert.Equal(1, fixture.Playback.Disposals);
    });

    [Fact]
    public Task FirstValidFrameOpensAnOwnedWindowAndStartsItsEntrance() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Window.Pulse(1000, .4);
        Assert.False(fixture.Window.IsVisible);
        fixture.Frame(1001);
        Assert.True(fixture.Window.IsVisible); Assert.Same(fixture.Owner, fixture.Window.Owner);
        Assert.Equal(0, fixture.Window.Opacity); Assert.NotNull(fixture.Image.Source);
        fixture.Window.Pulse(1001.125, .4);
        Assert.Equal(.5, fixture.Window.Opacity, 8); Assert.Equal(.4, fixture.Playback.Gain);
    });

    [Fact]
    public Task LiveGentleRetimesEntranceAndExitWithoutOpacityJumps() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0); fixture.Window.Pulse(.125, 1);
        var before = fixture.Window.Opacity;
        fixture.Settings.GentleVisuals = true; fixture.Window.Pulse(.125, 1);
        Assert.Equal(before, fixture.Window.Opacity);
        fixture.Settings.GentleVisuals = false; fixture.Window.Pulse(.5, 1);
        Assert.Equal(.75, fixture.Window.Opacity, 8);
        fixture.Window.Pulse(.875, 1); Assert.Equal(1, fixture.Window.Opacity);
        fixture.Playback.IsFinished = true; fixture.Window.Pulse(1, 1);
        fixture.Window.Pulse(1.125, 1); before = fixture.Window.Opacity;
        fixture.Settings.GentleVisuals = true; fixture.Window.Pulse(1.125, 1);
        Assert.Equal(before, fixture.Window.Opacity);
        fixture.Window.Pulse(1.5, 1); Assert.Equal(before / 2, fixture.Window.Opacity, 8);
        fixture.Window.Pulse(1.875, 1); Assert.True(fixture.Window.HasFinished);
    });

    [Fact]
    public Task NaturalOrCapCompletionAwardsOneSuccessAndDecoderFailureAwardsNone() => UiDispatcher.Run(() =>
    {
        using var success = new Fixture(); var finished = 0;
        success.Window.VideoFinished += () => finished++;
        success.Frame(0); success.Window.Pulse(.25, 1);
        success.Playback.IsFinished = true;
        success.Window.Pulse(1, 1); success.Window.Pulse(1.125, 1); success.Window.Pulse(1.25, 1);
        success.Window.Pulse(2, 1);
        Assert.Equal(1, finished); Assert.True(success.Window.HasFinished);
        using var failed = new Fixture(); var failures = 0;
        failed.Window.VideoFinished += () => failures++;
        failed.Frame(0); failed.Window.Pulse(.25, 1);
        failed.Playback.LastError = "Decoder failed"; failed.Playback.IsFinished = true;
        failed.Window.Pulse(1, 1); failed.Window.Pulse(1.25, 1);
        Assert.Equal(0, failures); Assert.Equal("Decoder failed", failed.Window.PlaybackError);
    });

    [Fact]
    public Task AttentionUsesVideoStartTimePausesAndResumesAfterAnAnswer() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(attention: true); var answers = new List<bool>();
        fixture.Window.CountAnswered += answers.Add;
        fixture.Frame(1000); fixture.Window.Pulse(1029.99, 1); Assert.False(fixture.Playback.Paused);
        fixture.Window.Pulse(1030, 1); Assert.True(fixture.Playback.Paused);
        var bubbles = fixture.Window.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "AttentionBubbles");
        Assert.False(fixture.Window.AnswerCount(0)); Assert.True(fixture.Playback.Paused);
        Assert.True(fixture.Window.AnswerCount(bubbles.Children.Count)); Assert.False(fixture.Playback.Paused);
        Assert.Equal(new[] { false, true }, answers);
        Assert.False(fixture.Window.AnswerCount(bubbles.Children.Count)); Assert.Equal(2, answers.Count);
        fixture.Window.Pulse(1060, 1); Assert.True(fixture.Playback.Paused);
        fixture.Window.Pulse(1075, 1); Assert.False(fixture.Playback.Paused);
        Assert.Equal(new[] { false, true, false }, answers);
        fixture.Window.FinishImmediately(); Assert.False(fixture.Window.AnswerCount(bubbles.Children.Count));
    });

    [Fact]
    public Task InvalidFramesFailOnlyTheirWindowWithoutMappingABlocker() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Playback.Frame = new([0], 960, 540, 1);
        fixture.Window.Pulse(0, 1);
        Assert.True(fixture.Window.HasFinished); Assert.False(fixture.Window.IsVisible);
        Assert.NotNull(fixture.Window.PlaybackError); Assert.Null(fixture.Image.Source);
    });

    [Fact]
    public Task HeldPanicRemainsOnePressAcrossFocusIntoTheOwnedVideo() => UiDispatcher.Run(() =>
    {
        var owner = new MainWindow(new(), windows: true, globalPanicAvailable: () => false);
        var playback = new Playback();
        var configure = typeof(MainWindow).GetMethod("ConfigureEffectInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var video = new VideoWindow(new(1, 0, 60, true, false, false), owner.Settings, playback,
            new LocalizationService(), owner.Session.Stop, window => configure.Invoke(owner, [window]));
        try
        {
            owner.Show(); owner.Activate(); owner.Session.Start();
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            owner.Session.Start(); video.Begin(owner, 0);
            playback.Frame = new([1, 2, 3, 255], 1, 1, 1); video.Pulse(0, 1);
            typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
            video.Activate(); Dispatcher.UIThread.RunJobs();
            video.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.True(owner.Session.IsRunning);
            video.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            video.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.False(owner.Session.IsRunning);
        }
        finally
        {
            video.FinishImmediately(); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            owner.Close(); Dispatcher.UIThread.RunJobs();
        }
    });

    private sealed class Fixture : IDisposable
    {
        public readonly ClassicSettings Settings = new();
        public readonly Window Owner = new();
        public readonly Playback Playback = new();
        public readonly SessionController Session;
        public readonly VideoWindow Window;
        public Image Image => ((Grid)Window.Content!).Children.OfType<Image>().Single();
        public Fixture(bool strict = false, bool attention = false)
        {
            Session = new(Settings);
            Window = new(new(1, 0, 60, !strict && !attention, strict, attention), Settings, Playback,
                new LocalizationService(), Session.Stop, _ => { });
            Session.StopRequested += Window.FinishImmediately;
            Owner.Show(); Session.Start(); Window.Begin(Owner, 0);
        }
        public void Frame(double now) { Playback.Frame = new([1, 2, 3, 255], 1, 1, 1); Window.Pulse(now, 1); }
        public void Dispose() { Window.FinishImmediately(); Owner.Close(); Session.Dispose(); Dispatcher.UIThread.RunJobs(); }
    }
    private sealed class Playback : ILocalVideoPlayback
    {
        public bool IsFinished { get; set; }
        public string? LastError { get; set; }
        public VideoFrame? Frame;
        public int Starts, Disposals;
        public bool Paused;
        public double Gain;
        public Action? Disposing;
        public void Start() => Starts++;
        public void SetGain(double gain) => Gain = gain;
        public void SetPaused(bool paused) => Paused = paused;
        public bool TryCopyFrame(out VideoFrame? frame) { frame = Frame; Frame = null; return frame is not null; }
        public void Dispose() { Disposing?.Invoke(); Disposals++; }
    }
}
