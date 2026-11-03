using System.Globalization;

namespace CCP.Classic.Core;

public enum CompanionEffect { Flash, Whisper, SpiralPulse }
public enum CompanionTriggerSource { TimedPhrase, ExplicitPhrase, Keyword }
// Seconds: the starter recording's length.
public sealed record CompanionPhrase(string Text, int AudioIndex, CompanionEffect? Effect, double Seconds = 2.5);
// Pick is set for a library line (docs/BARKS.md); AudioIndex then is -1. Seconds is the clip length.
public sealed record CompanionBarkCue(long Id, string Text, int AudioIndex, double Opacity, BarkPick? Pick = null, double Seconds = 0);
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
        new("Breathe", 1, null, 2.72), new("Relax", 2, CompanionEffect.Flash, 2.56),
        new("Focus", 0, CompanionEffect.Whisper, 2.24), new("Listen", 3, null, 1.44),
        new("Deeper", 4, CompanionEffect.SpiralPulse, 2.24)
    });
    // The bubble shows only approved words and curated library lines, never other text.
    public static bool IsApproved(string text) =>
        Phrases.Any(phrase => phrase.Text == text) || BarkLibrary.Default.IsApproved(text);
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
    // When set, idle words and clicks come from the bark library ("Idle", "AvatarClicked"); a null
    // answer (gap, cooldown, panic quiet) says nothing. Session words stay the approved five.
    public Func<string, BarkPick?>? Lines { get; set; }
    // A word or line is showing and has not started its exit.
    public bool IsSpeaking => bark is { Ending: false };
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
    // An event line at once, replacing a word still showing; the timer then waits a full interval.
    public bool Say(BarkPick pick, double now)
    {
        if (!IsRunning || !BarksVisible || !double.IsFinite(now)) return false;
        var at = Math.Max(lastTime, now);
        bark = Line(pick, at); nextPhrase = at + interval; return true;
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
            if (BarksVisible && (Lines is null ? Word(NextPhrase(), now) : Lines("AvatarClicked") is { } pick ? Line(pick, now) : null) is { } said)
            { bark = said; nextPhrase = now + interval; }
        }
        var nextInterval = BarkInterval;
        var enabled = TimerEnabled;
        if (nextInterval != interval || !timerActive || !enabled) nextPhrase = now + nextInterval;
        interval = nextInterval; timerActive = enabled;
        if (enabled && now >= nextPhrase)
        {
            nextPhrase = now + interval;
            if (IsIdle && Lines is not null)
            {
                if (BarksVisible && bark is null && Lines("Idle") is { } pick) bark = Line(pick, now);
            }
            else
            {
                var selected = NextPhrase();
                if (BarksVisible && bark is null) bark = Word(selected, now);
                if (phrasesEnabled && !IsIdle) QueuePhrase(selected.Text, CompanionTriggerSource.TimedPhrase);
            }
        }
        CompanionBarkCue? visible = null;
        if (bark is { } current)
        {
            current.Ending |= !BarksVisible || now >= current.Expires;
            var alpha = current.Sample(now, current.Ending ? 0 : 1, settings.GentleVisuals ? .75 : .25);
            if (current.Ending && alpha == 0) bark = null;
            else visible = new(current.Id, current.Text, current.AudioIndex, alpha, current.Pick, current.Seconds);
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
    private Bark Word(CompanionPhrase phrase, double now) =>
        new(++nextId, phrase.Text, phrase.AudioIndex, null, phrase.Seconds, now + 4, settings.GentleVisuals ? .75 : .25);
    // A recorded line stays up while it is spoken.
    private Bark Line(BarkPick pick, double now) => new(++nextId, pick.Line.Text, -1, pick, pick.Line.Seconds,
        now + Math.Max(4, pick.Line.Seconds + .5), settings.GentleVisuals ? .75 : .25);
    private CompanionPhrase NextPhrase() { var selected = Phrases[phrase]; phrase = (phrase + 1) % Phrases.Count; return selected; }
    private bool BarksVisible => settings.CompanionEnabled && settings.BarksEnabled;
    private bool TimerEnabled => BarksVisible || phrasesEnabled && !IsIdle;
    private double BarkInterval => settings.BarkIntervalSeconds > 0 ? Math.Clamp(settings.BarkIntervalSeconds, 1, 3600) : 60;
    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_'
        || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation;
    private sealed class Bark(long id, string text, int audioIndex, BarkPick? pick, double seconds, double expires, double initialFade)
    {
        public readonly long Id = id;
        public readonly string Text = text;
        public readonly int AudioIndex = audioIndex;
        public readonly BarkPick? Pick = pick;
        public readonly double Seconds = seconds;
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
