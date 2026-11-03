using CCP.Classic.App.Media;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private readonly LayeredAudioService layeredAudio = new();
    public LayeredAudioService LayeredAudio => layeredAudio;
    public string? LayeredAudioStatus => layeredAudio.StatusKey;
    private double LayeredMaster => Math.Clamp(settings.Effects["audio"].Values["master"] / 100, 0, 1);
    private void StartLayeredAudio()
    {
        if (settings.LayeredAudio.Enabled || IsAudioOnlySession)
            _ = layeredAudio.StartAsync(settings.LayeredAudio, LayeredMaster, IsAudioOnlySession);
    }
    private void TickLayeredAudio()
    {
        var ducked = settings.Audio.DuckingEnabled && (videos.IsPlaying || KeywordDucking || FlashDucking
            || audio.GetAudioSamples().Any(sample => sample.IsPlaying && sample.Gain > 0)
            || barkAudio.GetAudioSamples().Any(sample => sample.IsPlaying && sample.Gain > 0)
            || wipeAudio.GetAudioSamples().Any(sample => sample.IsPlaying && sample.Gain > 0)
            || drainAudio.GetAudioSamples().Any(sample => sample.IsPlaying && sample.Gain > 0));
        layeredAudio.UpdateGains(settings.LayeredAudio, LayeredMaster, ducked ? settings.Effects["audio"].Values["duck"] / 100 : 0);
    }
    public Task ApplyLayeredAudioAsync() => layeredAudio.ApplyAsync(settings.LayeredAudio, LayeredMaster, running && IsAudioOnlySession);
    public Task StopLayeredAudioAsync() => layeredAudio.StopAsync();
    private void DisposeLayeredAudio() => layeredAudio.Dispose();
}
