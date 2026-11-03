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
        companionHost = new(owner, settings, window => new(window, settings, configureInput), clock);
        companionHost.StopRequested += () => session.Stop();
        companionHost.PlacementChanged += () => CompanionPlacementChanged?.Invoke();
        companionHost.AvatarClicked += () => { AvatarClicked?.Invoke(); DeviceEvent?.Invoke(LocalDeviceEvent.Companion); };
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
        if (frame.Bark is { } bark && bark.Id != lastBark)
        {
            lastBark = bark.Id;
            DeviceEvent?.Invoke(LocalDeviceEvent.Companion);
            if (settings.Effects["audio"].Enabled && starter.Phrases[bark.AudioIndex].AudioPath is { } path)
                barkGate.Play(path, 1);
        }
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
    // A stop ends barks and talk at once; the tube stays and returns to idle.
    private void StopCompanion()
    {
        StopKeywords(); timedChanges.Stop(); companion.Stop();
        if (disposed) companionHost.Dispose(); else companionHost.EndSession();
    }
}
