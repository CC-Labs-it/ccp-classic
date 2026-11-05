using Avalonia.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ToyAttentionTests
{
    [Fact]
    public Task MirroredMutedVideoAcceptsOneExistingTargetAndRejectsAnEarlierQueuedPress() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(mirror: true); var answered = 0;
        fixture.First.CountAnswered += _ => answered++;
        fixture.Pulse(0); fixture.Pulse(60); Assert.Single(fixture.First.Targets);
        Assert.False(fixture.First.TryAcceptToyAttention(59));
        Assert.True(fixture.First.TryAcceptToyAttention(60)); Assert.False(fixture.Second!.TryAcceptToyAttention(60));
        Assert.Equal(1, answered); Assert.Empty(fixture.First.Targets); Assert.Empty(fixture.Second.Targets);
        Assert.Equal(0, fixture.Playback.Gain);
    });

    [Fact]
    public Task HiddenAndPausedInputCannotHitAndRestoredAttentionUsesItsPresentationClock() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Pulse(0); fixture.Pulse(60);
        fixture.Playback.Paused = true; Assert.False(fixture.First.TryAcceptToyAttention(60)); fixture.Playback.Paused = false;
        fixture.Owner.Hide(); fixture.Pulse(61); fixture.Pulse(300); Assert.False(fixture.First.TryAcceptToyAttention(300));
        fixture.Owner.Show(); fixture.Pulse(301); Assert.Single(fixture.First.Targets);
        Assert.False(fixture.First.TryAcceptToyAttention(61)); Assert.True(fixture.First.TryAcceptToyAttention(301));
        fixture.First.FinishImmediately(); Assert.False(fixture.First.TryAcceptToyAttention(301));
    });

    [Fact]
    public Task ATargetThatExpiredOrAnInvalidFloatingCheckCannotAcceptAToyPress() => UiDispatcher.Run(() =>
    {
        using (var fixture = new Fixture())
        { fixture.Pulse(0); fixture.Pulse(60); Assert.False(fixture.First.TryAcceptToyAttention(1000)); fixture.Pulse(1000); Assert.False(fixture.First.TryAcceptToyAttention(1000)); }
        using var floating = new Fixture(floating: true); floating.Pulse(0); floating.Pulse(60);
        Assert.False(floating.First.TryAcceptToyAttention(60));
    });

    [Fact]
    public Task AQueuedHiddenPressCannotUseAnOlderTargetAfterPresentationRestores() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); fixture.Pulse(0); fixture.Pulse(60); fixture.Pulse(65);
        Assert.Single(fixture.First.Targets); fixture.Owner.Hide(); fixture.Pulse(66); fixture.Pulse(300);
        fixture.Owner.Show(); fixture.Pulse(301); Assert.Single(fixture.First.Targets);
        Assert.False(fixture.First.TryAcceptToyAttention(300)); Assert.True(fixture.First.TryAcceptToyAttention(301));
    });

    private sealed class Fixture : IDisposable
    {
        public readonly Window Owner = new();
        public readonly Playback Playback = new();
        public readonly VideoWindow First;
        public readonly VideoWindow? Second;
        private readonly Playback? secondPlayback;
        private readonly SessionController session;
        public Fixture(bool mirror = false, bool floating = false)
        {
            var settings = new ClassicSettings(); settings.Video.TargetCount = 1; settings.Video.RandomTargets = false;
            session = new(settings); session.Start(); Owner.Show();
            var checks = new VideoAttentionTimeline(1); var cue = new VideoCue(1, 0, 60, floating, false, true);
            First = new(cue, settings, Playback, new LocalizationService(), session.Stop, _ => { }, checks); First.Begin(Owner, 0);
            if (mirror)
            { secondPlayback = new(); Second = new(cue, settings, secondPlayback, new LocalizationService(), session.Stop, _ => { }, checks); Second.Begin(Owner, 1); }
        }
        public void Pulse(double seconds)
        {
            Playback.Frame ??= new([1, 2, 3, 255], 1, 1, 1); First.Pulse(seconds, 0);
            if (Second is not null) { secondPlayback!.Frame ??= new([1, 2, 3, 255], 1, 1, 1); Second.Pulse(seconds, 0); }
        }
        public void Dispose() { First.FinishImmediately(); Second?.FinishImmediately(); Owner.Close(); session.Dispose(); }
    }
    private sealed class Playback : ILocalVideoPlayback
    {
        public bool IsFinished => false;
        public string? LastError => null;
        public VideoFrame? Frame;
        public bool Paused;
        public double Gain;
        public void Start() { }
        public void SetGain(double gain) => Gain = gain;
        public void SetPaused(bool paused) => Paused = paused;
        public AudioPlaybackSample? GetAudioSample() => new("local.mp4", 1, Gain, !Paused, TimeProvider.System.GetTimestamp());
        public bool TryCopyFrame(out VideoFrame? frame) { frame = Frame; Frame = null; return frame is not null; }
        public void Dispose() { }
    }
}
