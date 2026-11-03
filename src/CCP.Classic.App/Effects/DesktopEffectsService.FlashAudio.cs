using CCP.Classic.App.Media;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private readonly LibVlcAudioPlayback flashAudio = new();
    private readonly Queue<string> flashVoices = new();
    private readonly Random flashVoiceRandom = new();
    private AudioPlaybackTicket? flashVoice;
    private long flashVoiceBatch;
    private double flashVoiceStarted;
    private bool FlashDucking => flashVoice is { IsFinished: false };

    private bool BeginFlashVoice(long batch)
    {
        if (!running || !session.IsRunning || !settings.Flash.AudioEnabled || !settings.Effects["audio"].Enabled
            || settings.Effects["audio"].Values["master"] <= 0 || flashVoice is { IsFinished: false }) return false;
        if (flashVoices.Count == 0)
        {
            var local = audioLibrary?.IsCompletedSuccessfully == true
                ? audioLibrary.Result.Audio.Where(settings.Library.Includes) : [];
            foreach (var path in starter.Phrases.Select(phrase => phrase.AudioPath).OfType<string>().Concat(local)
                .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(_ => flashVoiceRandom.Next()).Take(2048))
                flashVoices.Enqueue(path);
        }
        if (flashVoices.Count == 0) return false;
        flashAudio.SetMasterGain(settings.Effects["audio"].Values["master"] / 100);
        flashVoice = flashAudio.PlayTracked(flashVoices.Dequeue(), 1);
        if (flashVoice is null) return false;
        flashVoiceBatch = batch; flashVoiceStarted = clock.GetElapsedTime(started).TotalSeconds; return true;
    }
    private void TickFlashVoice(double elapsed)
    {
        var enabled = running && settings.Flash.AudioEnabled && settings.Effects["audio"].Enabled
            && settings.Effects["audio"].Values["master"] > 0;
        flashAudio.SetMasterGain(enabled ? settings.Effects["audio"].Values["master"] / 100 : 0);
        if (flashVoice is null) return;
        if (!enabled || elapsed - flashVoiceStarted > 3600) flashAudio.StopAll();
        if (!flashVoice.IsFinished) return;
        timeline.CompleteFlashAudio(flashVoiceBatch); flashVoice = null; flashVoiceBatch = 0;
    }
    private void StopFlashVoice()
    {
        flashAudio.SetMasterGain(0); flashAudio.StopAll(); flashVoice = null; flashVoiceBatch = 0; flashVoices.Clear();
    }
}
