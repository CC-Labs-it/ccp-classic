using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioPlaybackGateTests
{
    [Fact]
    public void CuesStayBlockedUntilTheOwnerExplicitlyEnablesTheLane()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio);
        Assert.False(gate.Play("clip.wav", 1)); Assert.Null(gate.PlayTracked("clip.wav", 1));
        Assert.Empty(audio.Tickets); Assert.False(gate.IsEnabled);
        gate.Update(true, .7); Assert.True(gate.Play("clip.wav", 1));
        Assert.Equal(.7, audio.Master); Assert.True(gate.IsEnabled);
    }

    [Fact]
    public void MutingFinishesBothActiveAndPendingCuesWithoutRevivingThemOnReenable()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio); gate.Update(true, 1);
        var active = gate.PlayTracked("active.wav", .5)!;
        var queued = gate.PlayTracked("queued.wav", .8)!;
        gate.Update(false, 1);
        Assert.False(gate.IsEnabled); Assert.Equal(0, audio.Master); Assert.Equal(1, audio.Stops);
        Assert.True(active.IsFinished); Assert.False(active.WasSuccessful); Assert.True(queued.IsFinished);
        Assert.Null(gate.PlayTracked("muted.wav", 1));
        gate.Update(true, .5);
        Assert.Equal(2, audio.Tickets.Count); Assert.True(active.IsFinished); Assert.True(queued.IsFinished);
        var fresh = gate.PlayTracked("fresh.wav", 1)!;
        Assert.False(fresh.IsFinished); Assert.Equal(3, audio.Tickets.Count);
    }

    [Fact]
    public void DuckingToZeroKeepsAnEnabledVoiceAlive()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio); gate.Update(true, 1);
        var voice = gate.PlayTracked("clip.wav", 1)!;
        gate.Update(true, 0); Assert.True(gate.IsEnabled); Assert.Equal(0, audio.Stops); Assert.False(voice.IsFinished);
        gate.Update(true, 1); Assert.False(voice.IsFinished); Assert.Equal(1, audio.Master);
    }

    [Fact]
    public void PanicStopDrainsTheLaneAndRequiresANewEnableAndFreshCue()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio); gate.Update(true, 1);
        var voice = gate.PlayTracked("clip.wav", 1)!; gate.Stop();
        Assert.True(voice.IsFinished); Assert.Equal(0, audio.Master); Assert.False(gate.Play("clip.wav", 1));
        gate.Stop(); Assert.Equal(2, audio.Stops);
        gate.Update(true, 1); Assert.True(gate.Play("fresh.wav", 1)); Assert.Equal(2, audio.Tickets.Count);
    }

    [Fact]
    public void InvalidAndExcessGainNeverReachTheDeviceUnnormalized()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio); gate.Update(true, double.NaN);
        Assert.Equal(0, audio.Master);
        foreach (var gain in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        { Assert.False(gate.Play("clip.wav", gain)); Assert.Null(gate.PlayTracked("clip.wav", gain)); }
        Assert.Empty(audio.Tickets); gate.Update(true, 2); Assert.Equal(1, audio.Master);
        Assert.True(gate.Play("clip.wav", 2)); Assert.Equal(1, audio.LastGain);
        gate.Update(false, -1); Assert.Equal(0, audio.Master);
        gate.Update(false, 0); Assert.Equal(1, audio.Stops);
    }

    [Fact]
    public void ReplaceKeepsOneVoiceAndStillObeysTheLaneSwitch()
    {
        var audio = new Playback(); var gate = new AudioPlaybackGate(audio);
        Assert.False(gate.Replace("bark.wav", 1)); Assert.Empty(audio.Tickets);
        gate.Update(true, 1); Assert.True(gate.Replace("one.wav", 1));
        var first = Assert.Single(audio.Tickets); Assert.True(gate.Replace("two.wav", 1));
        Assert.True(first.IsFinished); Assert.False(audio.Tickets[1].IsFinished);
        Assert.False(gate.Replace("bark.wav", double.NaN)); Assert.False(gate.Replace("bark.wav", 0));
        gate.Stop(); Assert.False(gate.Replace("bark.wav", 1)); Assert.Equal(2, audio.Tickets.Count);
    }

    [Fact]
    public async Task AConcurrentDisableCannotLeaveASubmittedCueBehindTheStop()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var audio = new Playback { BeforePlay = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); } };
        var gate = new AudioPlaybackGate(audio); gate.Update(true, 1);
        var submit = Task.Run(() => gate.PlayTracked("clip.wav", 1), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var disable = Task.Run(() => gate.Update(false, 1), TestContext.Current.CancellationToken);
            release.Set(); var ticket = await submit.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await disable.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.NotNull(ticket); Assert.True(ticket.IsFinished); Assert.False(gate.IsEnabled);
            Assert.Null(gate.PlayTracked("late.wav", 1));
        }
        finally { release.Set(); }
    }

    private sealed class Playback : IAudioPlayback
    {
        public readonly List<AudioPlaybackTicket> Tickets = [];
        public double Master, LastGain;
        public int Stops;
        public Action? BeforePlay;
        public bool Play(string path, double gain) => PlayTracked(path, gain) is not null;
        public AudioPlaybackTicket? PlayTracked(string path, double gain)
        {
            BeforePlay?.Invoke(); LastGain = gain;
            var ticket = new AudioPlaybackTicket(); Tickets.Add(ticket); return ticket;
        }
        public void SetMasterGain(double gain) => Master = gain;
        public void StopAll() { Stops++; foreach (var ticket in Tickets) ticket.Complete(false); }
        public void Dispose() => StopAll();
    }
}
