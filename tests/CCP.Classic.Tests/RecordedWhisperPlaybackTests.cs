using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class RecordedWhisperPlaybackTests
{
    [Fact]
    public void MasterGainAppliesLiveWithoutReplayingTheClip()
    {
        var audio = new FakeAudio();
        var playback = new RecordedWhisperPlayback(audio);
        var settings = EnabledSettings();
        playback.Update(settings, true, "relax.mp3");
        settings.Effects["audio"].Values["master"] = 20;
        playback.Update(settings, false, null);
        Assert.Equal(new[] { .3, .1 }, audio.MasterGains);
        Assert.Equal(("relax.mp3", 1d), Assert.Single(audio.Plays));
        Assert.Equal(0, audio.Stops);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("sub")]
    [InlineData("whispers")]
    [InlineData("master")]
    public void DisablingAnyGateStopsOnceAndReenableNeedsANewCue(string gate)
    {
        var audio = new FakeAudio();
        var playback = new RecordedWhisperPlayback(audio);
        var settings = EnabledSettings();
        playback.Update(settings, true, "first.mp3");
        SetGate(settings, gate, false);
        playback.Update(settings, true, "blocked.mp3");
        playback.Update(settings, true, "still-blocked.mp3");
        Assert.Equal(1, audio.Stops);
        Assert.Single(audio.Plays);
        if (gate == "master") Assert.Equal(0, audio.MasterGains[^1]);
        SetGate(settings, gate, true);
        playback.Update(settings, false, "stale-cue.mp3");
        Assert.Single(audio.Plays);
        playback.Update(settings, true, "next.mp3");
        Assert.Equal(new[] { ("first.mp3", 1d), ("next.mp3", 1d) }, audio.Plays);
        Assert.Equal(1, audio.Stops);
    }

    [Fact]
    public void MissingRecordingsRemainSilentAndDisabledTicksDoNotStopRepeatedly()
    {
        var audio = new FakeAudio();
        var playback = new RecordedWhisperPlayback(audio);
        var settings = EnabledSettings();
        foreach (var clip in new string?[] { null, "", "  " }) playback.Update(settings, true, clip);
        Assert.Empty(audio.Plays);
        settings.Effects["sub"].Values["whispers"] = 0;
        for (var tick = 0; tick < 10; tick++) playback.Update(settings, true, "silent.mp3");
        Assert.Equal(1, audio.Stops);
        Assert.Empty(audio.Plays);
    }

    [Fact]
    public void StopClearsTheEnabledStateBeforeTheNextSession()
    {
        var audio = new FakeAudio();
        var playback = new RecordedWhisperPlayback(audio);
        var settings = EnabledSettings();
        playback.Update(settings, true, "first.mp3");
        playback.Stop();
        settings.Effects["audio"].Enabled = false;
        playback.Update(settings, false, null);
        Assert.Equal(1, audio.Stops);
        settings.Effects["audio"].Enabled = true;
        playback.Update(settings, false, "old.mp3");
        Assert.Single(audio.Plays);
        playback.Update(settings, true, "new.mp3");
        Assert.Equal(2, audio.Plays.Count);
    }

    private static ClassicSettings EnabledSettings()
    {
        var settings = new ClassicSettings();
        settings.Effects["audio"].Values["master"] = 60;
        settings.Effects["sub"].Values["whispers"] = 1;
        return settings;
    }

    private static void SetGate(ClassicSettings settings, string gate, bool value)
    {
        if (gate is "audio" or "sub") settings.Effects[gate].Enabled = value;
        else if (gate == "whispers") settings.Effects["sub"].Values[gate] = value ? 1 : 0;
        else settings.Effects["audio"].Values[gate] = value ? 60 : 0;
    }

    private sealed class FakeAudio : IAudioPlayback
    {
        internal readonly List<double> MasterGains = [];
        internal readonly List<(string Path, double Gain)> Plays = [];
        internal int Stops;
        public bool Play(string localPath, double gain) { Plays.Add((localPath, gain)); return true; }
        public void SetMasterGain(double gain) => MasterGains.Add(gain);
        public void StopAll() => Stops++;
        public void Dispose() { }
    }
}
