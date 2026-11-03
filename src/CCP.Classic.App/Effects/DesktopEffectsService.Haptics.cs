using Avalonia.Threading;
using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private readonly AudioEnvelopeCache hapticEnvelopes = new();
    private readonly AudioSyncDsp hapticDsp = new();
    private readonly LocalFunScriptPlayback funScript = new();
    private DispatcherTimer hapticTimer = null!;
    private long hapticSubId, hapticWipeId, hapticObservedAt;
    private bool hapticObserved, audioSyncUnavailable;
    public event Action<LocalDeviceEvent>? DeviceEvent;
    public event Action<double>? AudioLevelChanged;
    public event Action<AudioDspLevels>? AudioBandsChanged;
    public event Action<ScriptOutput>? ScriptLevelChanged;
    public event Action<double>? LuminanceLevelChanged;
    public event Action<double>? VideoLevelChanged;
    private void HapticFlashLuminance(IEnumerable<int> indices)
    {
        var level = running && settings.Devices.LuminanceSyncEnabled && selectedImages is { } library
            ? indices.Distinct().Take(16).Select(index => library.Brightness(index) ?? 0).DefaultIfEmpty().Max() * settings.Devices.LuminanceSyncIntensity : 0;
        LuminanceLevelChanged?.Invoke(level);
    }
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
        hapticDsp.Clear(); funScript.Clear(); hapticEnvelopes.Clear(); hapticSubId = hapticWipeId = 0;
        hapticObserved = audioSyncUnavailable = false;
        hapticTimer.Start();
    }
    private void TickHaptics(double elapsed)
    {
        if (!running || disposed) return;
        if (snapshot is { SubliminalId: > 0 } current && current.SubliminalId != hapticSubId)
        { hapticSubId = current.SubliminalId; DeviceEvent?.Invoke(LocalDeviceEvent.Subliminal); }
        var now = clock.GetTimestamp();
        if (hapticObserved && clock.GetElapsedTime(hapticObservedAt, now).TotalMilliseconds < 100) return;
        hapticObservedAt = now; hapticObserved = true;
        var videoSamples = videos.GetAudioSamples().ToArray();
        var timelineSamples = videos.GetTimelineSamples().ToArray();
        VideoLevelChanged?.Invoke(settings.Devices.VideoSyncEnabled && timelineSamples.Any(sample => sample.IsPlaying)
            ? settings.Devices.Level(LocalDeviceEvent.Video) : 0);
        var script = funScript.Sample(timelineSamples, clock, settings.Devices.FunScriptEnabled, settings.Devices.FunScriptToVibration);
        ScriptLevelChanged?.Invoke(script with { Intensity = script.Intensity * settings.Devices.FunScriptIntensity });
        var audioEnabled = settings.Devices.AudioSyncEnabled && (settings.Effects["audio"].Enabled || layeredAudio.IsPlaying)
            && settings.Effects["audio"].Values["master"] > 0;
        if (!audioEnabled) { hapticDsp.Clear(); audioSyncUnavailable = false; AudioLevelChanged?.Invoke(0); AudioBandsChanged?.Invoke(default); return; }
        var sources = audio.GetAudioSamples().Concat(wipeAudio.GetAudioSamples()).Concat(barkAudio.GetAudioSamples())
            .Concat(flashAudio.GetAudioSamples()).Concat(drainAudio.GetAudioSamples()).Concat(cueAudio.GetAudioSamples()).Concat(videoSamples)
            .Concat(layeredAudio.GetAudioSamples());
        var level = hapticDsp.Sample(sources, hapticEnvelopes.Get, clock, settings.Devices.AudioSyncDsp, out audioSyncUnavailable);
        AudioLevelChanged?.Invoke(level);
        AudioBandsChanged?.Invoke(hapticDsp.LastLevels);
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
        hapticTimer.Stop(); hapticDsp.Clear(); funScript.Clear(); hapticEnvelopes.Clear(); audioSyncUnavailable = false;
        AudioLevelChanged?.Invoke(0);
        AudioBandsChanged?.Invoke(default);
        ScriptLevelChanged?.Invoke(default);
        LuminanceLevelChanged?.Invoke(0);
        VideoLevelChanged?.Invoke(0);
    }
    private void DisposeHaptics()
    {
        StopHaptics(); videos.AttentionStarted -= HapticAttentionStarted; hapticEnvelopes.Dispose(); funScript.Dispose();
    }
}
