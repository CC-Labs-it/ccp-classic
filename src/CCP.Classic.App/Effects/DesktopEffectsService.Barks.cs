using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

// One spoken companion line, raised once when it starts: the seam for the avatar's emote controller.
// RuleId "word" marks one of the five approved session words (LineId is the word).
public sealed record CompanionLineSpoken(string RuleId, string LineId, string Mood, string Text, double Seconds);

// The bark system: real player actions -> BarkEngine -> the shared companion timeline -> the bark lane.
// Event keys and the curated lines are listed in docs/BARKS.md. Never synthetic speech: a line whose
// recording is missing is removed from the library before anything can pick it.
public sealed partial class DesktopEffectsService
{
    private static readonly int[] SessionMilestones = [15, 30, 60, 180];
    private BarkEngine barkEngine = null!;
    private BarkSwitchWatch barkSwitches = null!;
    private readonly string barkFolder = Path.Combine(AppContext.BaseDirectory, "Starter", "Barks");
    private readonly List<(string Trigger, Dictionary<string, double> Facts)> idleBarks = [];
    private readonly Queue<double> avatarClicks = new();
    private long barkEpoch;
    private int nextMilestone, sessionsToday, attentionMisses;
    private DateOnly sessionsDay;
    private double sessionLifetimeMinutes, lastSwitchChange = double.NaN;
    private bool videoWasPlaying, setupReadyAsked;
    public event Action<CompanionLineSpoken>? LineSpoken;
    // Progress facts the rules read: level, streak, lifetime_minutes, achievements_left.
    public Func<IReadOnlyDictionary<string, double>>? BarkFacts { get; set; }
    // Days since the previous launch; null on the very first launch. Only the run's first session reads it.
    public int? DaysAwayAtLaunch { get; set; }
    internal BarkEngine Barks => barkEngine;
    private double BarkNow => clock.GetElapsedTime(barkEpoch).TotalSeconds;

    private void InitializeBarks()
    {
        barkEpoch = clock.GetTimestamp();
        var library = new BarkLibrary(BarkLibrary.Default.Rules.Select(rule =>
            rule with { Lines = rule.Lines.Where(line => File.Exists(Path.Combine(barkFolder, line.Clip))).ToArray() }));
        barkEngine = new(library, () => BarkNow);
        barkSwitches = new(settings, () => session.PanicEnabled);
        companion.Lines = trigger => Pick(trigger, trigger == "AvatarClicked" ? new Dictionary<string, double> { ["clicks"] = avatarClicks.Count } : null);
        companionHost.IdleStarted += SayQueuedIdleBark;
        DeviceEvent += kind =>
        {
            if (kind == LocalDeviceEvent.Subliminal) Bark("Subliminal");
            else if (kind == LocalDeviceEvent.BrainDrain) Bark("BrainDrain");
        };
    }

    // A real player action happened. Says at most one line, now, if the tube is on screen.
    public bool Bark(string trigger, IReadOnlyDictionary<string, double>? facts = null)
    {
        if (disposed || !companion.IsRunning || companionHost.Window is not { CanPresent: true }) return false;
        if (Pick(trigger, facts, companion.IsSpeaking) is not { } pick) return false;
        // An idle line shows on the tube's next idle frame (at most 250 ms away).
        return companion.Say(pick, running ? clock.GetElapsedTime(started).TotalSeconds : companionHost.Now);
    }

    // Panic: the bark lane stops and nothing barks, clicks included, for BarkEngine.PanicQuietSeconds.
    public void SilenceBarks()
    {
        barkEngine.Panic(); idleBarks.Clear(); barkGate.Stop();
        if (!running && companion.IsSpeaking) { companion.Stop(); companionHost.IdleTick(); }
    }

    // Lines for the moment the idle tube is next on screen: the greeting, or a timed session's end.
    public void QueueIdleBark(string trigger, Dictionary<string, double>? facts = null)
    {
        idleBarks.Add((trigger, facts ?? [])); if (!running) SayQueuedIdleBark();
    }

    // Main-window clock (250 ms): the player's own switches, and the "ready, press start" nudge.
    public void PollBarks()
    {
        if (disposed) return;
        var flipped = barkSwitches.Poll();
        if (flipped.Count > 0) { lastSwitchChange = BarkNow; setupReadyAsked = false; }
        foreach (var trigger in flipped) if (Bark(trigger)) break;
        var quiet = BarkNow - lastSwitchChange;
        if (!running && !setupReadyAsked && quiet is >= 10 and < 15) { setupReadyAsked = true; Bark("SetupReady"); }
    }

    private BarkPick? Pick(string trigger, IReadOnlyDictionary<string, double>? facts, bool busy = false)
    {
        if (!settings.CompanionEnabled || !settings.BarksEnabled) return null;
        barkEngine.MinimumGapSeconds = Math.Min(BarkEngine.DefaultGapSeconds, Math.Max(1, settings.BarkIntervalSeconds));
        var merged = new Dictionary<string, double>(StringComparer.Ordinal) { ["running"] = running ? 1 : 0 };
        if (BarkFacts?.Invoke() is { } shared) foreach (var (key, value) in shared) merged[key] = value;
        if (facts is not null) foreach (var (key, value) in facts) merged[key] = value;
        return barkEngine.Raise(trigger, merged, busy);
    }

    private void SayQueuedIdleBark()
    {
        if (running || barkEngine.IsQuiet) { if (barkEngine.IsQuiet) idleBarks.Clear(); return; }
        if (idleBarks.Count == 0 || companionHost.Window is not { CanPresent: true } || !companion.IsIdle) return;
        var queued = idleBarks.ToArray(); idleBarks.Clear();
        foreach (var (trigger, facts) in queued) if (Bark(trigger, facts)) break;
    }

    private void CountAvatarClick()
    {
        var now = BarkNow; avatarClicks.Enqueue(now);
        while (avatarClicks.Count > 0 && now - avatarClicks.Peek() > 60) avatarClicks.Dequeue();
    }

    private void BeginSessionBarks()
    {
        // A greeting still waiting for the tube is stale once a session starts.
        barkEngine.BeginSession(); idleBarks.Clear(); nextMilestone = attentionMisses = 0; videoWasPlaying = false;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (sessionsDay != today) { sessionsDay = today; sessionsToday = 0; }
        sessionsToday++;
        var facts = new Dictionary<string, double> { ["hour"] = DateTime.Now.Hour, ["sessions_today"] = sessionsToday };
        if (sessionsToday == 1 && DaysAwayAtLaunch is { } away) facts["days_away"] = away;
        sessionLifetimeMinutes = BarkFacts?.Invoke().GetValueOrDefault("lifetime_minutes") ?? 0;
        Bark("SessionStarted", facts);
    }

    private void TickSessionBarks(double elapsed)
    {
        while (nextMilestone < SessionMilestones.Length && elapsed >= SessionMilestones[nextMilestone] * 60)
            Bark("SessionMinutes", new Dictionary<string, double> { ["minutes"] = SessionMilestones[nextMilestone++] });
        if (videos.IsPlaying && !videoWasPlaying) Bark("VideoStarted");
        videoWasPlaying = videos.IsPlaying;
    }

    private void AttentionBark(bool correct)
    {
        attentionMisses = correct ? 0 : attentionMisses + 1;
        Bark(correct ? "AttentionPass" : "AttentionFail", new Dictionary<string, double> { ["misses"] = attentionMisses });
    }

    // Only a session that ran its preset time to the end earns a line; a stop or a panic stays silent.
    private void QueueCompletionBark(double minutes)
    {
        idleBarks.Clear();
        idleBarks.Add(("SessionCompleted", new() { ["minutes"] = Math.Floor(minutes), ["lifetime_minutes"] = Math.Floor(sessionLifetimeMinutes) }));
    }

    private void InteractiveBark(InteractiveEffectEvent value)
    {
        if (value.Kind == InteractiveEffectEventKind.BubblePopped) Bark("BubblePopped");
        else if (value.Kind == InteractiveEffectEventKind.CornerHit) Bark("BouncingTextCorner");
        else if (value.Kind == InteractiveEffectEventKind.WipeCompleted) Bark("MindWipe");
    }

    private void RaiseLineSpoken(CompanionBarkCue bark)
    {
        var line = bark.Pick is { } pick
            ? new CompanionLineSpoken(pick.Rule.Id, pick.Line.LineId, pick.Rule.Mood, pick.Line.Text, pick.Line.Seconds)
            : new CompanionLineSpoken("word", bark.Text.ToLowerInvariant(), "", bark.Text, bark.Seconds);
        // The avatar's clips follow the line: a library line talks then reacts; one of the five
        // session words is a trigger word, one expressive clip and never the talking clips.
        if (bark.Pick is null) companionHost.TriggerWord();
        else companionHost.SpeakLine(line.LineId, line.Mood, line.Text, line.Seconds);
        LineSpoken?.Invoke(line);
    }
}
