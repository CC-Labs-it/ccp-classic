using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private CompanionTimeline companion = null!;
    private CompanionHost companionHost = null!;
    private long lastBark;
    private readonly TimedEffectTimeline timedChanges = new();
    public bool GlobalTextAvailable => panic.IsTextAvailable;
    public event Action? AvatarClicked;
    public event Action? CompanionPlacementChanged;

    private void InitializeCompanion()
    {
        companion = new(settings);
        InitializeKeywords();
        companionHost = new(owner, settings, window => new(window, settings, configureInput), clock, companion);
        companionHost.StopRequested += () => session.Stop();
        companionHost.PlacementChanged += () => CompanionPlacementChanged?.Invoke();
        // Idle words carry no device pulse and no effect: those stay session behaviour.
        companionHost.IdleBark += bark => { if (!running) { RefreshIdleBarkGate(); SpeakBark(bark); } };
        companionHost.AvatarClicked += () =>
        {
            if (running) companion.Poke(clock.GetElapsedTime(started).TotalSeconds);
            AvatarClicked?.Invoke(); DeviceEvent?.Invoke(LocalDeviceEvent.Companion);
        };
    }
    private void StartCompanion()
    {
        companionHost.BeginSession();
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        companion.Start(); keywords.Start(); lastBark = 0;
        timedChanges.Start(settings.TimedChanges);
    }
    public void FeedText(string characters) => FeedKeywords(characters);
    public bool TriggerPhrase(string phrase)
    {
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        return companion.TriggerPhrase(phrase);
    }
    private void TickCompanion(double elapsed)
    {
        TickKeywords(elapsed);
        foreach (var change in timedChanges.Step(elapsed)) settings.Effects[change.EffectId].Enabled = change.Enabled;
        companion.TriggerPhrasesEnabled = settings.TriggerPhrasesEnabled;
        var frame = companion.Step(elapsed);
        companionHost.SessionFrame(frame.Bark);
        if (frame.Bark is { } bark && bark.Id != lastBark) { DeviceEvent?.Invoke(LocalDeviceEvent.Companion); SpeakBark(bark); }
        foreach (var trigger in frame.Triggers)
        {
            var triggered = trigger.Effect switch
            {
                CompanionEffect.Flash => timeline.TriggerFlash(),
                CompanionEffect.Whisper => timeline.TriggerWhisper(),
                CompanionEffect.SpiralPulse => timeline.TriggerSpiralPulse(),
                _ => false
            };
            if (triggered && trigger.Source == CompanionTriggerSource.Keyword) DeviceEvent?.Invoke(LocalDeviceEvent.Keyword);
        }
    }
    // One voice for idle and session words: the approved recording only, never synthetic speech;
    // a word with no recording stays text. A new word replaces one still speaking.
    private void SpeakBark(CompanionBarkCue bark)
    {
        if (bark.Id == lastBark) return;
        lastBark = bark.Id;
        if (settings.Effects["audio"].Enabled && starter.Phrases[bark.AudioIndex].AudioPath is { } path)
            barkGate.Replace(path, 1);
    }
    // Outside a session nothing else opens the bark lane, and a muted switch must silence it at once.
    private void RefreshIdleBarkGate()
    {
        if (running || disposed) return;
        var gain = AudioMix(out var audioEnabled);
        UpdateBarkGate(audioEnabled, gain);
    }
    // A stop ends barks and talk at once; the tube stays and returns to idle.
    private void StopCompanion()
    {
        StopKeywords(); timedChanges.Stop(); companion.Stop();
        if (disposed) companionHost.Dispose(); else companionHost.EndSession();
    }
}
