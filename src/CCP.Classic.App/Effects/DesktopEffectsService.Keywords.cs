using System.Diagnostics;
using System.Runtime.InteropServices;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private KeywordTriggerTimeline keywords = null!;
    private readonly LibVlcAudioPlayback keywordAudio = new();
    private readonly Queue<KeywordAction> keywordAudioQueue = new();
    private AudioPlaybackTicket? keywordTicket;
    private KeywordAction? keywordPlaying;
    private int keywordRepeats;
    private double keywordNextAudio;
    private bool manualKeywordAudio;
    public event Action<int>? KeywordXp;
    public event Action<double>? KeywordHaptic;
    private bool KeywordDucking => keywordTicket is { IsFinished: false } && keywordPlaying?.DuckAudio == true;

    private void InitializeKeywords()
    {
        keywords = new(settings); panic.TextReceived += FeedKeywords;
    }
    private void FeedKeywords(string characters)
    {
        if (settings.Keywords.IgnoreOwnFocus && (owner.IsActive || owner.OwnedWindows.Any(window => window.IsActive))) { keywords.Clear(); return; }
        var app = "";
        if (settings.Keywords.AppScope != KeywordAppScope.Everywhere || settings.Keywords.Triggers.Any(t => t.AppScope != KeywordAppScope.Everywhere))
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                    using var process = Process.GetProcessById((int)pid); app = process.ProcessName;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            else if (owner.IsActive) app = "CCP.Classic.App";
        }
        keywords.FeedText(characters, app);
    }
    private void TickKeywords(double elapsed)
    {
        var fired = keywords.Step(elapsed);
        if (fired.Count > 0) Bark("KeywordTriggered");
        DispatchKeywordActions(fired);
        TickKeywordAudio(elapsed);
    }
    public bool TestKeyword(KeywordDefinition trigger)
    {
        if (!running || !session.IsRunning || disposed) return false;
        manualKeywordAudio = true;
        DispatchKeywordActions(trigger.Actions.Where(a => a.Enabled).Take(16).Select(a => new KeywordActionCue(trigger.Keyword, a)));
        return true;
    }
    public int ImportKeywordPhrases()
    {
        var local = audioLibrary?.IsCompletedSuccessfully == true ? audioLibrary.Result.Audio.Where(settings.Library.Includes) : [];
        return settings.Keywords.ImportPhrases(settings.Subliminal.Phrases.Select(phrase =>
            new SubliminalPhrase(phrase.Text, RecordedPhraseResolver.Resolve(phrase, starter.Phrases, local).AudioPath)));
    }
    private void DispatchKeywordActions(IEnumerable<KeywordActionCue> actions)
    {
        foreach (var cue in actions)
        {
            var action = cue.Action;
            switch (action.Kind)
            {
                case KeywordActionKind.Visual:
                    _ = action.Visual switch
                    {
                        KeywordVisual.Flash => timeline.TriggerFlash(),
                        KeywordVisual.Subliminal => timeline.TriggerSubliminal(),
                        KeywordVisual.ExactText => timeline.TriggerSubliminal(cue.Keyword),
                        KeywordVisual.Pink => timeline.TriggerPinkPulse(),
                        KeywordVisual.Spiral => timeline.TriggerSpiralPulse(),
                        KeywordVisual.MindWipe => lockEffects.TriggerMindWipe(),
                        KeywordVisual.LockCard => lockEffects.TriggerLock(),
                        KeywordVisual.Whisper => timeline.TriggerWhisper(Math.Max(0, settings.Subliminal.Phrases.FindIndex(
                            phrase => phrase.Text.Equals(cue.Keyword, StringComparison.OrdinalIgnoreCase)))),
                        KeywordVisual.BrainDrain => TriggerBrainDrainPulse(),
                        KeywordVisual.Bubbles => interactive.TriggerBubbles(),
                        KeywordVisual.Video => videos.TryTriggerBubble(),
                        _ => false
                    }; break;
                case KeywordActionKind.Audio:
                    if (keywordAudioQueue.Count < 16 && SubliminalOptions.LocalAudio(action.AudioPath) is not null)
                        keywordAudioQueue.Enqueue(new() { Kind = action.Kind, AudioPath = action.AudioPath, Gain = action.Gain,
                            PlayCount = action.PlayCount, DelayMs = action.DelayMs, DuckAudio = action.DuckAudio });
                    break;
                case KeywordActionKind.Haptic: KeywordHaptic?.Invoke(action.Gain); break;
                case KeywordActionKind.Xp: KeywordXp?.Invoke((int)Math.Round(action.Xp * settings.Keywords.SessionXpMultiplier)); break;
            }
        }
    }
    private void TickKeywordAudio(double elapsed)
    {
        var enabled = settings.Effects["audio"].Enabled && settings.Effects["audio"].Values["master"] > 0;
        keywordAudio.SetMasterGain(enabled ? settings.Effects["audio"].Values["master"] / 100 : 0);
        if (!enabled || !settings.KeywordTriggersEnabled && !manualKeywordAudio) { StopKeywordAudio(); return; }
        if (keywordTicket is { } ticket)
        {
            if (!ticket.IsFinished) return;
            keywordTicket = null;
            if (!ticket.WasSuccessful) keywordRepeats = 0;
            keywordNextAudio = elapsed + (keywordPlaying?.DelayMs ?? 0) / 1000d;
        }
        if (elapsed < keywordNextAudio) return;
        if (keywordRepeats <= 0)
        {
            keywordPlaying = keywordAudioQueue.TryDequeue(out var next) ? next : null;
            keywordRepeats = keywordPlaying?.PlayCount ?? 0;
            if (keywordPlaying is null) manualKeywordAudio = false;
        }
        if (keywordPlaying?.AudioPath is { } path && keywordRepeats > 0)
        {
            keywordRepeats--; keywordTicket = keywordAudio.PlayTracked(path, keywordPlaying.Gain);
            if (keywordTicket is null) keywordRepeats = 0;
        }
    }
    private void StopKeywordAudio()
    { keywordAudio.StopAll(); keywordTicket = null; keywordPlaying = null; keywordRepeats = 0; keywordNextAudio = 0; keywordAudioQueue.Clear(); manualKeywordAudio = false; }
    private void StopKeywords() { keywords.Stop(); StopKeywordAudio(); }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
