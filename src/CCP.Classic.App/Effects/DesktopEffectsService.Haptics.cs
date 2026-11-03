using Avalonia.Threading;
using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private readonly AudioEnvelopeCache hapticEnvelopes = new();
    private DispatcherTimer hapticTimer = null!;
    private long hapticSubId, hapticWipeId, hapticObservedAt;
    private bool hapticObserved, hapticBounce, hapticDrain, audioSyncUnavailable;
    public event Action<LocalDeviceEvent>? DeviceEvent;
    public event Action<double>? AudioLevelChanged;
    public string? HapticStatus => audioSyncUnavailable ? "AudioSyncUnavailable" : null;

    private void InitializeHaptics()
    {
        hapticTimer = new(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => TickHaptics(clock.GetElapsedTime(started).TotalSeconds));
        videos.AttentionStarted += HapticAttentionStarted;
    }
    private void HapticAttentionStarted() => DeviceEvent?.Invoke(LocalDeviceEvent.Attention);
    private void StartHaptics()
    {
        hapticEnvelopes.Clear(); hapticSubId = hapticWipeId = 0;
        hapticObserved = hapticBounce = hapticDrain = audioSyncUnavailable = false;
        hapticTimer.Start();
    }
    private void TickHaptics(double elapsed)
    {
        if (!running || disposed) return;
        if (snapshot is { SubliminalId: > 0 } current && current.SubliminalId != hapticSubId)
        { hapticSubId = current.SubliminalId; DeviceEvent?.Invoke(LocalDeviceEvent.Subliminal); }
        var bounce = settings.Effects["bounce"].Enabled && interactiveFrame.BouncingText is { Opacity: > .001 };
        if (bounce && !hapticBounce) DeviceEvent?.Invoke(LocalDeviceEvent.BouncingText);
        hapticBounce = bounce;
        var drain = settings.Effects["drain"].Enabled && lockFrame.BrainDrain is { Intensity: > 0, Opacity: > .001 };
        if (drain && !hapticDrain) DeviceEvent?.Invoke(LocalDeviceEvent.BrainDrain);
        hapticDrain = drain;
        var now = clock.GetTimestamp();
        if (hapticObserved && clock.GetElapsedTime(hapticObservedAt, now).TotalMilliseconds < 100) return;
        hapticObservedAt = now; hapticObserved = true;
        var audioEnabled = settings.Devices.AudioSyncEnabled && settings.Effects["audio"].Enabled
            && settings.Effects["audio"].Values["master"] > 0;
        if (!audioEnabled) { audioSyncUnavailable = false; AudioLevelChanged?.Invoke(0); return; }
        var sources = audio.GetAudioSamples().Concat(wipeAudio.GetAudioSamples()).Concat(barkAudio.GetAudioSamples())
            .Concat(cueAudio.GetAudioSamples()).Concat(videos.GetAudioSamples());
        var level = SampleAudioLevel(sources, hapticEnvelopes.Get, clock, out audioSyncUnavailable);
        AudioLevelChanged?.Invoke(level);
    }
    internal static double SampleAudioLevel(IEnumerable<AudioPlaybackSample> sources, Func<string, RmsEnvelope> envelope,
        TimeProvider clock, out bool unavailable)
    {
        unavailable = false; var level = 0d;
        foreach (var source in sources.Take(64))
        {
            if (!source.IsPlaying || !double.IsFinite(source.Gain) || source.Gain <= 0 || !double.IsFinite(source.SourceSeconds)
                || source.SourceSeconds < 0 || clock.GetElapsedTime(source.ObservedAt).TotalMilliseconds is < 0 or >= 300) continue;
            var value = envelope(source.Path); unavailable |= value.Failed;
            level = Math.Max(level, value.Sample(source.SourceSeconds) * Math.Clamp(source.Gain, 0, 1));
        }
        return Math.Clamp(level, 0, 1);
    }
    private void HapticWipeStarted(long id)
    {
        if (id <= 0 || id == hapticWipeId) return;
        hapticWipeId = id; DeviceEvent?.Invoke(LocalDeviceEvent.MindWipe);
    }
    private void StopHaptics()
    {
        hapticTimer.Stop(); hapticEnvelopes.Clear(); audioSyncUnavailable = false;
        AudioLevelChanged?.Invoke(0);
    }
    private void DisposeHaptics()
    {
        StopHaptics(); videos.AttentionStarted -= HapticAttentionStarted; hapticEnvelopes.Dispose();
    }
}
