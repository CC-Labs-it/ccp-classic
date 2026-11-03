namespace CCP.Classic.App.Media;

// The caller owns the device. This gate serializes policy changes and cue submission only.
internal sealed class AudioPlaybackGate(IAudioPlayback playback)
{
    private readonly object gate = new();
    private bool enabled;
    public bool IsEnabled { get { lock (gate) return enabled; } }

    // enabled includes the user's mute/feature switches. Zero gain alone may be temporary ducking.
    public void Update(bool nextEnabled, double masterGain)
    {
        var gain = double.IsFinite(masterGain) ? Math.Clamp(masterGain, 0, 1) : 0;
        lock (gate)
        {
            var stop = enabled && !nextEnabled;
            enabled = nextEnabled;
            playback.SetMasterGain(enabled ? gain : 0);
            if (stop) playback.StopAll();
        }
    }

    public bool Play(string localPath, double gain)
    {
        lock (gate) return enabled && double.IsFinite(gain) && gain > 0 && playback.Play(localPath, Math.Min(gain, 1));
    }

    public AudioPlaybackTicket? PlayTracked(string localPath, double gain)
    {
        lock (gate) return enabled && double.IsFinite(gain) && gain > 0 ? playback.PlayTracked(localPath, Math.Min(gain, 1)) : null;
    }

    public void Stop()
    {
        lock (gate)
        {
            enabled = false; playback.SetMasterGain(0); playback.StopAll();
        }
    }
}
