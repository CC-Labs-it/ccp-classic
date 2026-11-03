using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

internal sealed class RecordedWhisperPlayback(IAudioPlayback audio)
{
    private bool enabled;

    internal void Update(ClassicSettings settings, bool cue, string? clip, double masterScale = 1, bool manual = false)
    {
        var master = settings.Effects["audio"].Values["master"] / 100;
        master = double.IsFinite(master) ? Math.Clamp(master * masterScale * settings.Subliminal.WhisperVolume, 0, 1) : 0;
        audio.SetMasterGain(master);
        var next = settings.Effects["audio"].Enabled && (settings.Effects["sub"].Enabled || manual)
            && settings.Effects["sub"].Values["whispers"] != 0 && master > 0;
        if (enabled && !next) audio.StopAll();
        enabled = next;
        if (enabled && cue && !string.IsNullOrWhiteSpace(clip)) audio.Play(clip, 1);
    }

    internal void Stop()
    {
        enabled = false;
        audio.StopAll();
    }
}
