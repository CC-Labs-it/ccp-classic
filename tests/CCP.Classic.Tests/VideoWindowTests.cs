using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    public Task OriginalCornerMuteNeverEndsTheVideoAndCloseUsesThePanicGate() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0); fixture.Window.Pulse(.25, .6);
        var controls = fixture.Window.GetVisualDescendants().OfType<Button>().ToArray();
        var mute = controls.Single(c => c.Name == "VideoMute"); var close = controls.Single(c => c.Name == "VideoClose");
        Assert.Equal(24, mute.Width); Assert.Equal(.6, mute.Opacity);
        mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, fixture.Playback.Gain); Assert.True(fixture.Session.IsRunning); Assert.Null(fixture.Window.GetAudioSample());
        Assert.NotNull(fixture.Window.GetTimelineSample());
        fixture.Window.Pulse(.5, .6); Assert.Equal(0, fixture.Playback.Gain);
        mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(.6, fixture.Playback.Gain);
        fixture.Session.SetPanicEnabled(false, true); close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(fixture.Session.IsRunning);
        fixture.Session.SetPanicEnabled(true); close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(fixture.Session.IsRunning);
        using var strict = new Fixture(strict: true); strict.Frame(0);
        Assert.Single(strict.Window.GetVisualDescendants().OfType<Button>(), c => c.Name == "VideoMute");
        Assert.DoesNotContain(strict.Window.GetVisualDescendants().OfType<Button>(), c => c.Name == "VideoClose");
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
    public Task OriginalAttentionTargetsMoveWithoutPausingAndCountEachClickOnce() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(attention: true); fixture.Settings.Video.TargetCount = 1;
        var answers = new List<bool>(); var prompts = 0;
        fixture.Window.CountAnswered += answers.Add; fixture.Window.AttentionStarted += () => prompts++;
        fixture.Frame(1000); fixture.Window.Pulse(1001, 1); Assert.Empty(fixture.Window.Targets);
        fixture.Window.Pulse(1060, 1); var target = Assert.Single(fixture.Window.Targets);
        Assert.False(fixture.Playback.Paused); Assert.NotNull(fixture.Window.GetAudioSample()); Assert.Equal(1, prompts);
        fixture.Window.Pulse(1060.1, 1); Assert.NotEqual(target.X, Assert.Single(fixture.Window.Targets).X);
        Assert.True(fixture.Window.HitTarget(target.Id)); Assert.False(fixture.Window.HitTarget(target.Id));
        Assert.Empty(fixture.Window.Targets); Assert.Equal(new[] { true }, answers);
        fixture.Window.FinishImmediately(); Assert.False(fixture.Window.HitTarget(target.Id));
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
    public Task VisibleFloatingVideoContinuesWhenTheWholeWindowGroupLosesFocus() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0);
        var deactivate = typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        deactivate.Invoke(fixture.Owner, null); deactivate.Invoke(fixture.Window, null);
        var copies = fixture.Playback.Copies; fixture.Frame(1);
        Assert.False(fixture.Owner.IsActive); Assert.False(fixture.Window.IsActive);
        Assert.True(fixture.Window.IsVisible); Assert.False(fixture.Playback.Paused);
        Assert.Equal(copies + 1, fixture.Playback.Copies);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OwnerHidingOrMinimizingPausesDecodingAndRestoresTheLatestFrame(bool minimize) => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0); fixture.Window.Pulse(.25, 1);
        var copies = fixture.Playback.Copies;
        if (minimize) fixture.Owner.WindowState = WindowState.Minimized; else fixture.Owner.Hide();
        fixture.Frame(1); var latest = fixture.Playback.Frame; fixture.Window.Pulse(200, 1);
        Assert.True(fixture.Playback.Paused); Assert.False(fixture.Window.IsVisible);
        Assert.Equal(copies, fixture.Playback.Copies); Assert.Same(latest, fixture.Playback.Frame);
        if (minimize) fixture.Owner.WindowState = WindowState.Normal; else fixture.Owner.Show();
        fixture.Window.Pulse(201, 1);
        Assert.True(fixture.Window.IsVisible); Assert.False(fixture.Playback.Paused);
        Assert.Equal(copies + 1, fixture.Playback.Copies); Assert.Null(fixture.Playback.Frame);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TheVideoWindowItselfCanBeHiddenOrMinimizedWithoutCopyingFrames(bool minimize) => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Frame(0); var copies = fixture.Playback.Copies;
        if (minimize) fixture.Window.WindowState = WindowState.Minimized; else fixture.Window.Hide();
        fixture.Frame(1); fixture.Window.Pulse(200, 1);
        Assert.True(fixture.Playback.Paused); Assert.Equal(copies, fixture.Playback.Copies);
        if (!minimize)
        {
            fixture.Owner.Hide(); fixture.Window.Pulse(201, 1); fixture.Owner.Show(); fixture.Window.Pulse(202, 1);
            Assert.False(fixture.Window.IsVisible);
        }
        if (minimize) fixture.Window.WindowState = WindowState.Normal; else fixture.Window.Show(fixture.Owner);
        fixture.Window.Pulse(203, 1);
        Assert.False(fixture.Playback.Paused); Assert.Equal(copies + 1, fixture.Playback.Copies);
    });

    [Fact]
    public Task HiddenFirstFrameWaitsWithoutCopyingMappingOrConsumingTheWatchdog() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Owner.Hide(); fixture.Frame(0);
        fixture.Window.Pulse(1000, 1);
        Assert.True(fixture.Playback.Paused); Assert.Equal(0, fixture.Playback.Copies);
        Assert.False(fixture.Window.IsVisible); Assert.False(fixture.Window.HasFinished);
        Assert.Null(fixture.Window.PlaybackError); Assert.Null(fixture.Image.Source);
        fixture.Owner.Show(); fixture.Window.Pulse(1001, 1);
        Assert.False(fixture.Playback.Paused); Assert.True(fixture.Window.IsVisible);
        Assert.NotNull(fixture.Image.Source); Assert.False(fixture.Window.HasFinished);
    });

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public Task AWorkerThatAlreadyFinishedReleasesItsHiddenViewWithoutCopyingOrRestoring(bool minimize, bool failed) => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var successes = 0; fixture.Window.VideoFinished += () => successes++;
        fixture.Frame(0); fixture.Window.Pulse(.25, 1); var copies = fixture.Playback.Copies;
        fixture.Playback.IsFinished = true; if (failed) fixture.Playback.LastError = "Decoder failed";
        if (minimize) fixture.Window.WindowState = WindowState.Minimized; else fixture.Window.Hide();
        fixture.Window.Pulse(1, 1);
        Assert.True(fixture.Window.HasFinished); Assert.False(fixture.Window.IsVisible); Assert.Null(fixture.Image.Source);
        Assert.Equal(copies, fixture.Playback.Copies); Assert.Equal(1, fixture.Playback.Disposals);
        Assert.Equal(failed ? 0 : 1, successes); Assert.Equal(failed ? "Decoder failed" : null, fixture.Window.PlaybackError);
        fixture.Window.Pulse(100, 1); Assert.Equal(1, fixture.Playback.Disposals);
    });

    [Fact]
    public Task HiddenAttentionFreezesItsDeadlineAndRestoringResumesPlayback() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(attention: true); fixture.Settings.Video.TargetCount = 1;
        var results = new List<bool>(); fixture.Window.CountAnswered += results.Add;
        fixture.Frame(0); fixture.Window.Pulse(60, 1); Assert.Single(fixture.Window.Targets);
        fixture.Owner.Hide(); fixture.Window.Pulse(61, 1); fixture.Window.Pulse(300, 1);
        Assert.True(fixture.Playback.Paused); fixture.Owner.Show(); fixture.Window.Pulse(301, 1);
        Assert.False(fixture.Playback.Paused); Assert.Empty(results); Assert.Single(fixture.Window.Targets);
        fixture.Window.Pulse(312.9, 1); Assert.Empty(results);
        fixture.Window.Pulse(313, 1); Assert.Equal(new[] { false }, results); Assert.Empty(fixture.Window.Targets);
    });

    [Fact]
    public Task HiddenTargetsCannotBeClickedAndAVisibleHitKeepsTheDecoderRunning() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(attention: true); fixture.Settings.Video.TargetCount = 1;
        fixture.Frame(0); fixture.Window.Pulse(60, 1); var target = Assert.Single(fixture.Window.Targets);
        fixture.Owner.Hide(); fixture.Window.Pulse(61, 1);
        Assert.False(fixture.Window.HitTarget(target.Id)); Assert.True(fixture.Playback.Paused);
        fixture.Owner.Show(); fixture.Window.Pulse(300, 1);
        Assert.True(fixture.Window.HitTarget(target.Id)); Assert.False(fixture.Playback.Paused);
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
        public Image Image => ((Grid)Window.Content!).Children.OfType<Image>().Single(item => item.Name == "VideoImage");
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
        public int Starts, Disposals, Copies;
        public bool Paused;
        public double Gain;
        public Action? Disposing;
        public void Start() => Starts++;
        public void SetGain(double gain) => Gain = gain;
        public void SetPaused(bool paused) => Paused = paused;
        public AudioPlaybackSample? GetAudioSample() => new("test.wav", 1, Gain, !Paused, TimeProvider.System.GetTimestamp());
        public bool TryCopyFrame(out VideoFrame? frame) { Copies++; frame = Frame; Frame = null; return frame is not null; }
        public void Dispose() { Disposing?.Invoke(); Disposals++; }
    }
}
