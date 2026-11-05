using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCP.Classic.Core;

public enum CompanionEmoteKind { Idle, Talk, Reaction }
public sealed record CompanionEmoteStep(double At, string Clip, CompanionEmoteKind Kind);
public sealed record CompanionIdleClip(string Clip, int Weight);
public sealed record CompanionClipTiming(int SpeakStartMs, int SpeakEndMs, int DurationMs);

// CCP's avatar0 emote rules (Resources/avatar0_emotes/emotes.json and clip_timing.json), trimmed to the
// parts that drive clip choice. Layout and per-clip scale stay with Classic's owner-tuned tube.
public sealed class CompanionEmoteMap
{
    public int DefaultClipMs { get; private init; } = 7200;
    public double FadeSeconds { get; private init; } = 1;
    public double TalkStartDelaySeconds { get; private init; } = 1;
    public double TalkLeadOutSeconds { get; private init; } = .5;
    public double MinClipSeconds { get; private init; } = 2.5;
    public double NonverbalMaxSeconds { get; private init; } = 1.4;
    public int MaxTalkClips { get; private init; } = 3;
    public IReadOnlyList<CompanionIdleClip> Idle { get; private init; } = [];
    public IReadOnlyList<string> TalkPool { get; private init; } = [];
    public IReadOnlyList<string> Expressive { get; private init; } = [];
    public IReadOnlyList<string> ClickEmotes { get; private init; } = [];
    // Longest prefix first, so "video_check_pass" wins over "video_".
    public IReadOnlyList<KeyValuePair<string, string>> StemPrefixes { get; private init; } = [];
    public IReadOnlyDictionary<string, string> Moods { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, CompanionClipTiming> Timing { get; private init; } = new Dictionary<string, CompanionClipTiming>();
    // Every clip the rules can name. Each one must ship as Assets/Companion/<clip>.gif.
    public IReadOnlySet<string> Clips { get; private init; } = new HashSet<string>();

    public double NativeSeconds(string clip) =>
        (Timing.TryGetValue(clip, out var timing) && timing.DurationMs > 0 ? timing.DurationMs : DefaultClipMs) / 1000d;

    public static CompanionEmoteMap Parse(string emotesJson, string timingJson)
    {
        using var emotes = JsonDocument.Parse(emotesJson);
        using var timingDocument = JsonDocument.Parse(timingJson);
        var root = emotes.RootElement;
        var idle = root.TryGetProperty("idleRotation", out var rotation)
            ? rotation.EnumerateArray().Select(item => new CompanionIdleClip(item.GetProperty("clip").GetString() ?? "idle",
                item.TryGetProperty("weight", out var weight) ? Math.Max(1, weight.GetInt32()) : 1)).ToList()
            : [];
        if (idle.Count == 0) idle.Add(new("idle", 1));
        var talking = root.TryGetProperty("talking", out var talk) ? talk : default;
        var expressive = root.TryGetProperty("expressive", out var express) ? express : default;
        var stems = root.TryGetProperty("stemPrefix", out var stem)
            ? stem.EnumerateObject().Select(pair => new KeyValuePair<string, string>(pair.Name.ToLowerInvariant(), pair.Value.GetString() ?? ""))
                .Where(pair => pair.Value.Length > 0).OrderByDescending(pair => pair.Key.Length).ToList()
            : [];
        var moods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("mood", out var mood))
            foreach (var pair in mood.EnumerateObject()) if (pair.Value.GetString() is { Length: > 0 } clip) moods[pair.Name] = clip;
        var timing = new Dictionary<string, CompanionClipTiming>(StringComparer.Ordinal);
        foreach (var pair in timingDocument.RootElement.EnumerateObject())
        {
            if (pair.Name.StartsWith('_') || pair.Value.ValueKind != JsonValueKind.Object) continue;
            timing[pair.Name] = new(Int(pair.Value, "speakStartMs", 0), Int(pair.Value, "speakEndMs", 0), Int(pair.Value, "durationMs", 0));
        }
        var talkPool = List(talking, "pool"); var expressivePool = List(expressive, "pool"); var clicks = List(root, "clickEmotes");
        var clips = new HashSet<string>(StringComparer.Ordinal);
        clips.UnionWith(idle.Select(item => item.Clip)); clips.UnionWith(talkPool); clips.UnionWith(expressivePool);
        clips.UnionWith(clicks); clips.UnionWith(stems.Select(pair => pair.Value)); clips.UnionWith(moods.Values);
        return new CompanionEmoteMap
        {
            DefaultClipMs = Math.Max(1, Int(root, "clipMs", 7200)),
            FadeSeconds = Math.Max(0, Int(root, "fadeMs", 1000)) / 1000d,
            TalkStartDelaySeconds = Math.Max(0, Int(root, "talkStartDelayMs", 1000)) / 1000d,
            TalkLeadOutSeconds = Math.Max(0, Int(root, "talkLeadOutMs", 500)) / 1000d,
            MinClipSeconds = Math.Max(0, Int(root, "minClipMs", 2500)) / 1000d,
            MaxTalkClips = Math.Max(1, Int(talking, "maxTalkClips", 3)),
            NonverbalMaxSeconds = expressive.ValueKind == JsonValueKind.Object && expressive.TryGetProperty("nonverbalMaxSec", out var max)
                ? max.GetDouble() : 1.4,
            Idle = idle.AsReadOnly(), TalkPool = talkPool, Expressive = expressivePool, ClickEmotes = clicks,
            StemPrefixes = stems.AsReadOnly(), Moods = moods, Timing = timing, Clips = clips
        };
    }

    private static int Int(JsonElement element, string name, int fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number : fallback;
    private static IReadOnlyList<string> List(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString()).OfType<string>().Where(clip => clip.Length > 0).Distinct().ToList().AsReadOnly()
            : [];
}

// CCP's tube emote controller (AvatarTube/AvatarTubeWindow.CirceEmotes.cs) as a pure planner on one
// monotonic clock in seconds. Every clip plays ONCE; the window crossfades to whatever Clip names next.
// Idle is a weighted rotation that never repeats the clip on screen. A spoken line plays talk clips sized
// to its length, then one reaction (line id stem, then mood, then a random expressive clip). Non-verbal
// sounds and single trigger words play one expressive clip with no mouth movement.
public sealed partial class CompanionEmoteDirector(CompanionEmoteMap map, Random? random = null)
{
    // Owner 2026-11-03: every avatar animation 20% slower than before. The clips carry 60 ms GIF frames
    // (16.7 fps, 7.2 s a clip); Classic plays them at 75 ms (13.3 fps, 9.0 s a clip). Speaking windows
    // stretch with the clip; pacing in wall time (talk delay, lead out, holds) stays CCP's.
    public const double PlaybackRate = .8;
    public const double ClickCooldownSeconds = 3;
    // A click or interrupting swap keeps the clip on screen at least this long (CCP CirceMinHoldMs).
    public const double SwapHoldSeconds = 2;
    private const double TalkFastStartSeconds = .7, MinTalkWindowSeconds = .5, FallbackStartSeconds = .6, FallbackLengthSeconds = 2.5;
    private readonly Random random = random ?? new Random();
    private readonly List<CompanionEmoteStep> schedule = [];
    private double lastClick = double.NegativeInfinity;

    public CompanionEmoteMap Map => map;
    public string Clip { get; private set; } = "";
    public CompanionEmoteKind Kind { get; private set; }
    public double ClipStarted { get; private set; }
    // Changes whenever a clip (re)starts, including the same clip played again.
    public long Generation { get; private set; }
    public bool IsStarted => Generation > 0;
    public IReadOnlyList<CompanionEmoteStep> Pending => schedule;
    public bool IsSpeaking => Kind == CompanionEmoteKind.Talk || schedule.Any(step => step.Kind == CompanionEmoteKind.Talk);
    public double ClipSeconds(string clip) => map.NativeSeconds(clip) / PlaybackRate;
    // Where a clip started at started stands at now, in its own native seconds.
    public static double PositionOf(double started, double now) => Math.Max(0, now - started) * PlaybackRate;

    public void Start(double now) { schedule.Clear(); Show(PickIdle(), now, CompanionEmoteKind.Idle); }

    // Advances to the clip that should be on screen at now.
    public string Step(double now)
    {
        if (!IsStarted) Start(now);
        while (schedule.Count > 0 && schedule[0].At <= now)
        {
            var step = schedule[0]; schedule.RemoveAt(0); Show(step.Clip, step.At, step.Kind);
        }
        // With nothing scheduled, a finished clip hands over to the idle rotation. A clip that ends before
        // its scheduled successor holds its last frame. A long gap (hidden tube) starts afresh at now.
        for (var guard = 0; schedule.Count == 0 && guard < 64; guard++)
        {
            var end = ClipStarted + ClipSeconds(Clip);
            if (now < end) break;
            Show(PickIdle(), now - end > 1 ? now : end, CompanionEmoteKind.Idle);
        }
        return Clip;
    }

    // A left click: one of the affectionate click emotes, once per cooldown. It interrupts a spoken line.
    public bool Click(double now)
    {
        if (!IsStarted) Start(now);
        if (now - lastClick < ClickCooldownSeconds) return false;
        var pool = map.ClickEmotes.Where(clip => clip != Clip).ToList();
        if (pool.Count == 0) pool = [.. map.ClickEmotes];
        if (pool.Count == 0) return false;
        lastClick = now; schedule.Clear();
        schedule.Add(new(now + Math.Max(0, SwapHoldSeconds - (now - ClipStarted)), pool[random.Next(pool.Count)], CompanionEmoteKind.Reaction));
        return true;
    }

    // A spoken line. seconds is the recording's length; 0 estimates it from the text.
    public void Line(double now, string? lineId, string? mood, string? text, double seconds)
    {
        if (!IsStarted) Start(now);
        var duration = seconds > 0 && double.IsFinite(seconds) ? seconds : EstimateSeconds(text);
        if (IsNonverbal(text, seconds)) { ReactionOnly(PickExpressive() ?? PickIdle(), now); return; }
        var talk = PlanTalk(duration);
        var reaction = ResolveReaction(lineId, mood) ?? PickExpressive() ?? PickIdle();
        TalkSequence(talk, reaction, duration, now);
    }

    // A giggle or other short sound: one expressive clip held for its full length, no mouth movement.
    public void NonVerbal(double now)
    {
        if (!IsStarted) Start(now);
        ReactionOnly(PickExpressive() ?? PickIdle(), now);
    }

    // A single trigger word or repetitive trigger sound: one expressive clip, never talk, and only from
    // the idle rotation. A line, click or reaction already playing keeps the avatar.
    public bool TriggerWord(double now)
    {
        if (!IsStarted) Start(now);
        Step(now);
        if (Kind != CompanionEmoteKind.Idle || schedule.Count > 0) return false;
        ReactionOnly(PickExpressive() ?? PickIdle(), now);
        return true;
    }

    // A stop or panic: talk and reactions end now and the idle rotation returns.
    public void Stop(double now)
    {
        if (!IsStarted) { Start(now); return; }
        schedule.Clear();
        if (Kind != CompanionEmoteKind.Idle) Show(PickIdle(), now, CompanionEmoteKind.Idle);
    }

    public static double EstimateSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 2.5;
        var words = text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(.45 * words + .8, 2, 7);
    }

    // Text present: only non-verbal tokens (giggles, mmm, sighs). No text: a very short recording.
    public bool IsNonverbal(string? text, double seconds) => !string.IsNullOrWhiteSpace(text)
        ? NonverbalText().IsMatch(text.Trim())
        : seconds > 0 && seconds <= map.NonverbalMaxSeconds;

    public string? ResolveReaction(string? lineId, string? mood)
    {
        if (!string.IsNullOrEmpty(lineId))
        {
            var stem = lineId.ToLowerInvariant();
            foreach (var pair in map.StemPrefixes) if (stem.StartsWith(pair.Key, StringComparison.Ordinal)) return pair.Value;
        }
        if (!string.IsNullOrWhiteSpace(mood) && map.Moods.TryGetValue(mood.Split(',')[0].Trim(), out var clip)) return clip;
        return null;
    }

    // Talk clips whose speaking windows cover the line, capped at maxTalkClips. The first opens its mouth
    // promptly; the rest are shuffled for variety. The runtime cuts the last one at the line's end.
    public IReadOnlyList<string> PlanTalk(double seconds)
    {
        var pool = map.TalkPool;
        if (pool.Count == 0) return ["talkA"];
        var snappy = pool.Where(clip => Timing(clip).Start <= TalkFastStartSeconds).ToList();
        var first = snappy.Count > 0 ? snappy[random.Next(snappy.Count)] : pool.OrderBy(clip => Timing(clip).Start).First();
        var picks = new List<string> { first }; var covered = Length(first);
        var rest = pool.Where(clip => clip != first).ToList();
        for (var index = rest.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1); (rest[index], rest[swap]) = (rest[swap], rest[index]);
        }
        foreach (var clip in rest)
        {
            if (covered >= seconds || picks.Count >= map.MaxTalkClips) break;
            picks.Add(clip); covered += Length(clip);
        }
        return picks;
    }

    // The speaking window of a talk clip in wall seconds at the playback rate.
    public (double Start, double End) Timing(string clip)
    {
        if (!map.Timing.TryGetValue(clip, out var timing))
            return (FallbackStartSeconds / PlaybackRate, (FallbackStartSeconds + FallbackLengthSeconds) / PlaybackRate);
        var duration = timing.DurationMs > 0 ? timing.DurationMs : FallbackLengthSeconds * 1000;
        var start = Math.Clamp(timing.SpeakStartMs, 0, Math.Max(0, duration - 1));
        var end = timing.SpeakEndMs > start ? Math.Min(timing.SpeakEndMs, duration) : duration;
        return (start / 1000d / PlaybackRate, end / 1000d / PlaybackRate);
    }

    private double Length(string clip) { var (start, end) = Timing(clip); return end - start; }

    // The first talk clip joins talkStartDelay into the voice (later if the clip on screen still needs its
    // minimum hold). Each next clip arrives so its mouth is open as the previous one's talking ends, and the
    // reaction crossfades in talkLeadOut before the line ends. A line too short for a talk window plays
    // only its reaction.
    private void TalkSequence(IReadOnlyList<string> talk, string reaction, double seconds, double now)
    {
        schedule.Clear();
        var startDelay = Math.Max(RemainingHold(now), map.TalkStartDelaySeconds);
        if (seconds - startDelay - map.TalkLeadOutSeconds < MinTalkWindowSeconds) { ReactionOnly(reaction, now); return; }
        var begin = now + startDelay;
        var deadline = seconds - map.TalkLeadOutSeconds - startDelay;
        schedule.Add(new(begin, talk[0], CompanionEmoteKind.Talk));
        double covered = Timing(talk[0]).End, lastStart = 0;
        for (var index = 1; index < talk.Count; index++)
        {
            var (start, end) = Timing(talk[index]);
            var at = Math.Max(lastStart + map.MinClipSeconds, covered - start);
            if (at >= deadline) break;
            schedule.Add(new(begin + at, talk[index], CompanionEmoteKind.Talk));
            lastStart = at; covered = at + end;
        }
        // A line longer than the planned clips repeats the last one once.
        if (covered + .25 < deadline)
        {
            var at = Math.Max(lastStart + map.MinClipSeconds, covered - Timing(talk[^1]).Start);
            if (at < deadline) schedule.Add(new(begin + at, talk[^1], CompanionEmoteKind.Talk));
        }
        schedule.Add(new(begin + deadline, reaction, CompanionEmoteKind.Reaction));
    }

    private void ReactionOnly(string clip, double now)
    {
        schedule.Clear();
        schedule.Add(new(now + RemainingHold(now), clip, CompanionEmoteKind.Reaction));
    }

    private double RemainingHold(double now) => IsStarted ? Math.Clamp(map.MinClipSeconds - (now - ClipStarted), 0, map.MinClipSeconds) : 0;

    private void Show(string clip, double at, CompanionEmoteKind kind) { Clip = clip; ClipStarted = at; Kind = kind; Generation++; }

    private string? PickExpressive()
    {
        var pool = map.Expressive.Where(clip => clip != Clip).ToList();
        if (pool.Count == 0) pool = [.. map.Expressive];
        return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
    }

    private string PickIdle()
    {
        var pool = map.Idle.Where(item => item.Clip != Clip).ToList();
        if (pool.Count == 0) pool = [.. map.Idle];
        if (pool.Count == 0) return "idle";
        var roll = random.Next(pool.Sum(item => item.Weight));
        foreach (var item in pool) { roll -= item.Weight; if (roll < 0) return item.Clip; }
        return pool[0].Clip;
    }

    // Whole line is only non-verbal tokens: *giggles*, mmm, mhm, hmm, ahh, ooh, haa, moans, sighs, teehee,
    // gasps, purrs, ohh, uh, possibly repeated. Not "giggle I love being...". CCP's pattern, unchanged.
    [GeneratedRegex(@"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NonverbalText();
}
