using System.Globalization;

namespace CCP.Classic.Core;

public enum CompanionEffect { Flash, Whisper, SpiralPulse }
public enum CompanionTriggerSource { TimedPhrase, ExplicitPhrase, Keyword }
public sealed record CompanionPhrase(string Text, int AudioIndex, CompanionEffect? Effect);
public sealed record CompanionBarkCue(long Id, string Text, int AudioIndex, double Opacity);
public sealed record CompanionTriggerCue(long Id, CompanionEffect Effect, CompanionTriggerSource Source);
public sealed record CompanionFrame(CompanionBarkCue? Bark, IReadOnlyList<CompanionTriggerCue> Triggers)
{
    public static CompanionFrame Empty { get; } = new(null, []);
}

// Only the approved words and recordings are represented here. No typed history is retained.
public sealed class CompanionTimeline(ClassicSettings settings)
{
    public static IReadOnlyList<CompanionPhrase> Phrases { get; } = Array.AsReadOnly(new CompanionPhrase[]
    {
        new("Breathe", 1, null), new("Relax", 2, CompanionEffect.Flash),
        new("Focus", 0, CompanionEffect.Whisper), new("Listen", 3, null),
        new("Deeper", 4, CompanionEffect.SpiralPulse)
    });
    private readonly List<CompanionTriggerCue> pending = [];
    private readonly char[] suffix = new char[6];
    private Bark? bark;
    private double lastTime, nextPhrase, interval;
    private int phrase, suffixLength;
    private long nextId;
    private bool oversizedWord, timerActive, phrasesEnabled, poked;
    private double lastPoke = double.NegativeInfinity;
    // Spam clicks never stack words or recordings.
    public const double PokeCooldownSeconds = 1.5;
    public bool IsRunning { get; private set; }
    // Idle barks speak words only: no effect triggers and no keyword input outside a session.
    public bool IsIdle { get; private set; }
    public bool TriggerPhrasesEnabled
    {
        get => phrasesEnabled;
        set
        {
            phrasesEnabled = value;
            if (!value) pending.RemoveAll(cue => cue.Source != CompanionTriggerSource.Keyword);
        }
    }

    public void Start()
    {
        Stop(); IsRunning = true; lastTime = 0; phrase = 0;
        interval = BarkInterval; nextPhrase = interval; timerActive = TimerEnabled;
    }
    // The idle clock is not zero based: the first word waits one full interval from now.
    public void StartIdle(double now)
    {
        var next = phrase; Stop(); IsRunning = IsIdle = true; phrase = next;
        lastTime = double.IsFinite(now) ? now : 0;
        interval = BarkInterval; nextPhrase = lastTime + interval; timerActive = TimerEnabled;
    }
    public void Stop()
    {
        IsRunning = IsIdle = poked = false; bark = null; lastPoke = double.NegativeInfinity;
        pending.Clear(); ClearKeywordInput();
    }

    // A click on the avatar: the next approved word at once, never an effect trigger.
    public bool Poke(double now)
    {
        if (!IsRunning || !BarksVisible || !double.IsFinite(now)) return false;
        var at = Math.Max(lastTime, now);
        if (at - lastPoke < PokeCooldownSeconds) return false;
        lastPoke = at; poked = true; return true;
    }
    public void ClearKeywordInput()
    {
        Array.Clear(suffix); suffixLength = 0; oversizedWord = false;
        pending.RemoveAll(cue => cue.Source == CompanionTriggerSource.Keyword);
    }

    public bool TriggerPhrase(string text)
    {
        if (!IsRunning || IsIdle || !phrasesEnabled) return false;
        return QueuePhrase(text, CompanionTriggerSource.ExplicitPhrase);
    }

    public void FeedText(string text)
    {
        if (!IsRunning || IsIdle || !settings.KeywordTriggersEnabled) { ClearKeywordInput(); return; }
        foreach (var character in text)
        {
            if (character is '\b' or '\u001b') { Array.Clear(suffix); suffixLength = 0; oversizedWord = false; continue; }
            if (IsWord(character))
            {
                if (suffixLength < suffix.Length) suffix[suffixLength++] = character;
                else oversizedWord = true;
            }
            else
            {
                if (!oversizedWord && suffixLength > 0)
                    QueuePhrase(new string(suffix, 0, suffixLength), CompanionTriggerSource.Keyword);
                Array.Clear(suffix); suffixLength = 0; oversizedWord = false;
            }
        }
    }

    public CompanionFrame Step(double seconds)
    {
        if (!IsRunning) return CompanionFrame.Empty;
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime; lastTime = now;
        if (!settings.KeywordTriggersEnabled) ClearKeywordInput();
        if (bark is { } expired && now >= expired.EndsAt) bark = null;
        if (poked)
        {
            // Replaces a word still showing; the timer then waits a full interval after it.
            poked = false;
            if (BarksVisible) { bark = new(++nextId, NextPhrase(), now + 4, settings.GentleVisuals ? .75 : .25); nextPhrase = now + interval; }
        }
        var nextInterval = BarkInterval;
        var enabled = TimerEnabled;
        if (nextInterval != interval || !timerActive || !enabled) nextPhrase = now + nextInterval;
        interval = nextInterval; timerActive = enabled;
        if (enabled && now >= nextPhrase)
        {
            nextPhrase = now + interval;
            var selected = NextPhrase();
            if (BarksVisible && bark is null) bark = new(++nextId, selected, now + 4, settings.GentleVisuals ? .75 : .25);
            if (phrasesEnabled && !IsIdle) QueuePhrase(selected.Text, CompanionTriggerSource.TimedPhrase);
        }
        CompanionBarkCue? visible = null;
        if (bark is { } current)
        {
            current.Ending |= !BarksVisible || now >= current.Expires;
            var alpha = current.Sample(now, current.Ending ? 0 : 1, settings.GentleVisuals ? .75 : .25);
            if (current.Ending && alpha == 0) bark = null;
            else visible = new(current.Id, current.Phrase.Text, current.Phrase.AudioIndex, alpha);
        }
        var triggers = Array.AsReadOnly(pending.ToArray()); pending.Clear();
        return new(visible, triggers);
    }

    private bool QueuePhrase(string text, CompanionTriggerSource source)
    {
        var selected = Phrases.FirstOrDefault(item => string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase));
        if (selected?.Effect is not { } effect || pending.Count >= 16) return false;
        pending.Add(new(++nextId, effect, source)); return true;
    }
    private CompanionPhrase NextPhrase() { var selected = Phrases[phrase]; phrase = (phrase + 1) % Phrases.Count; return selected; }
    private bool BarksVisible => settings.CompanionEnabled && settings.BarksEnabled;
    private bool TimerEnabled => BarksVisible || phrasesEnabled && !IsIdle;
    private double BarkInterval => settings.BarkIntervalSeconds > 0 ? Math.Clamp(settings.BarkIntervalSeconds, 1, 3600) : 60;
    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_'
        || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation;
    private sealed class Bark(long id, CompanionPhrase phrase, double expires, double initialFade)
    {
        public readonly long Id = id;
        public readonly CompanionPhrase Phrase = phrase;
        public readonly double Expires = expires;
        public double EndsAt = expires + initialFade;
        public bool Ending;
        private double exitFade = initialFade;
        private double from, target, value, started, duration;
        public double Sample(double now, double next, double fade)
        {
            if (!Ending && fade > exitFade) { exitFade = fade; EndsAt = Expires + exitFade; }
            if (next != target)
            {
                from = value; target = next; started = next == 0 && now >= Expires ? Expires : now;
                duration = next == 0 ? Math.Max(exitFade, fade) : fade;
                if (next == 0) EndsAt = started + duration;
            }
            else if (fade > duration)
            {
                from = value; started = now; duration = fade;
                if (next == 0) EndsAt = now + duration;
            }
            var t = duration > 0 ? Math.Clamp((now - started) / duration, 0, 1) : 1;
            return value = from + (target - from) * t * t * (3 - 2 * t);
        }
    }
}
