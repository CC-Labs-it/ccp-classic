using CCP.Classic.App.Companion;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private CompanionTimeline companion = null!;
    private CompanionWindow? companionWindow;
    private long lastBark;
    public bool GlobalTextAvailable => panic.IsTextAvailable;
    public event Action? AvatarClicked;

    private void InitializeCompanion()
    {
        companion = new(settings);
        panic.TextReceived += companion.FeedText;
    }
    private void StartCompanion()
    {
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        companion.Start(); lastBark = 0;
    }
    public void FeedText(string characters) => companion.FeedText(characters);
    public bool TriggerPhrase(string phrase)
    {
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        return companion.TriggerPhrase(phrase);
    }
    private void TickCompanion(double elapsed)
    {
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        var frame = companion.Step(elapsed);
        if (settings.CompanionEnabled && companionWindow is null)
        {
            companionWindow = new(owner, settings, configureInput);
            companionWindow.StopRequested += () => session.Stop();
            companionWindow.AvatarClicked += () => AvatarClicked?.Invoke();
        }
        companionWindow?.Update(frame.Bark, elapsed);
        if (frame.Bark is { } bark && bark.Id != lastBark)
        {
            lastBark = bark.Id;
            if (settings.Effects["audio"].Enabled && starter.Phrases[bark.AudioIndex].AudioPath is { } path)
                cueAudio.Play(path, 1);
        }
        foreach (var trigger in frame.Triggers)
        {
            switch (trigger.Effect)
            {
                case CompanionEffect.Flash: timeline.TriggerFlash(); break;
                case CompanionEffect.Whisper: timeline.TriggerWhisper(); break;
                case CompanionEffect.SpiralPulse: timeline.TriggerSpiralPulse(); break;
            }
        }
    }
    private void StopCompanion()
    {
        companion.Stop(); companionWindow?.FinishImmediately(); companionWindow = null;
    }
}
