using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class CompanionEmoteDirectorTests
{
    private static readonly CompanionEmoteMap Map = LoadMap();
    private static readonly string[] IdleClips = ["idle", "idle3", "idle4", "idle5", "tender", "shy", "adoring", "blowkiss", "celebrate", "entrancing"];

    [Fact]
    public void ShippedRulesMatchCcpAvatarZero()
    {
        Assert.Equal(1, Map.FadeSeconds); Assert.Equal(1, Map.TalkStartDelaySeconds); Assert.Equal(.5, Map.TalkLeadOutSeconds);
        Assert.Equal(2.5, Map.MinClipSeconds); Assert.Equal(1.4, Map.NonverbalMaxSeconds); Assert.Equal(3, Map.MaxTalkClips);
        Assert.Equal(IdleClips, Map.Idle.Select(item => item.Clip));
        Assert.Equal(new[] { 6, 6, 6, 6, 1, 1, 1, 1, 1, 1 }, Map.Idle.Select(item => item.Weight));
        Assert.Equal(new[] { "talkA", "talkB", "talkC", "talkD", "talkE", "talkF" }, Map.TalkPool);
        Assert.Equal(new[] { "giggle", "sultry", "wink", "tease", "blowkiss", "adoring", "flirt", "shy" }, Map.Expressive);
        Assert.Equal(new[] { "shy", "sultry", "adoring", "tender", "blowkiss" }, Map.ClickEmotes);
        // idle2 ships in CCP but no rule names it, so Classic leaves it out.
        Assert.Equal(27, Map.Clips.Count); Assert.DoesNotContain("idle2", Map.Clips);
    }

    // Owner 2026-11-03: every animation 20% slower. The clips carry 60 ms frames (16.7 fps, 7.2 s a clip).
    [Fact]
    public void ClipsPlayAtEightyPercentOfTheirOldRate()
    {
        const double frame = .06, oldFps = 1 / frame;
        Assert.Equal(.8, CompanionEmoteDirector.PlaybackRate);
        var newFps = oldFps * CompanionEmoteDirector.PlaybackRate;
        Assert.Equal(16.667, oldFps, 3); Assert.Equal(13.333, newFps, 3);
        // 0.9 s of wall time used to reach frame 15; now it reaches frame 12.
        Assert.Equal(15, (int)(.9 / frame + 1e-9));
        Assert.Equal(12, (int)(CompanionEmoteDirector.PositionOf(10, 10.9) / frame + 1e-9));
        var director = new CompanionEmoteDirector(Map, new Random(1));
        Assert.Equal(7.2, Map.NativeSeconds("idle")); Assert.Equal(9, director.ClipSeconds("idle"), 9);
        // Speaking windows stretch with the clip: 250 to 7000 ms becomes 0.3125 to 8.75 s.
        var (start, end) = director.Timing("talkA"); Assert.Equal(.3125, start, 9); Assert.Equal(8.75, end, 9);
    }

    [Fact]
    public void IdleRotationReachesEveryWeightedClipAndNeverRepeatsTheClipOnScreen()
    {
        var director = new CompanionEmoteDirector(Map, new Random(7));
        director.Start(0);
        var seen = new List<string> { director.Clip };
        for (var index = 0; index < 3000; index++)
        {
            var end = director.ClipStarted + director.ClipSeconds(director.Clip);
            director.Step(end - .01); Assert.Equal(seen[^1], director.Clip);
            director.Step(end);
            Assert.Equal(end, director.ClipStarted); Assert.Equal(CompanionEmoteKind.Idle, director.Kind);
            Assert.NotEqual(seen[^1], director.Clip);
            seen.Add(director.Clip);
        }
        var counts = IdleClips.ToDictionary(clip => clip, clip => seen.Count(item => item == clip));
        Assert.All(counts.Values, count => Assert.True(count > 0));
        Assert.Equal(seen.Count, counts.Values.Sum());
        foreach (var heavy in IdleClips[..4])
            foreach (var light in IdleClips[4..]) Assert.True(counts[heavy] > 3 * counts[light], $"{heavy} {counts[heavy]} vs {light} {counts[light]}");
    }

    [Fact]
    public void TalkClipCountFollowsTheLineLength()
    {
        var director = new CompanionEmoteDirector(Map, new Random(3));
        // One clip speaks for 8.4375 s at the slowed rate.
        foreach (var (seconds, count) in new[] { (2d, 1), (8d, 1), (8.4, 1), (10d, 2), (16.8, 2), (17.5, 3), (60d, 3) })
        {
            var talk = director.PlanTalk(seconds);
            Assert.Equal(count, talk.Count); Assert.Equal(talk.Count, talk.Distinct().Count());
            Assert.All(talk, clip => Assert.Contains(clip, Map.TalkPool));
        }
    }

    [Fact]
    public void ASpokenLineTalksThenPlaysOneReactionAndReturnsToIdle()
    {
        var director = new CompanionEmoteDirector(Map, new Random(5));
        director.Start(0);
        director.Line(10, "level_up_3", null, "You did so well today", 6);
        // Talk joins one second into the voice; the reaction crossfades in half a second before it ends.
        var plan = director.Pending;
        Assert.Equal(2, plan.Count);
        Assert.Equal(new CompanionEmoteStep(11, plan[0].Clip, CompanionEmoteKind.Talk), plan[0]);
        Assert.Equal(new CompanionEmoteStep(15.5, "celebrate", CompanionEmoteKind.Reaction), plan[1]);
        Assert.True(director.IsSpeaking);
        director.Step(11); Assert.Contains(director.Clip, Map.TalkPool); Assert.Equal(CompanionEmoteKind.Talk, director.Kind);
        director.Step(15.5); Assert.Equal("celebrate", director.Clip); Assert.False(director.IsSpeaking);
        director.Step(15.5 + 9); Assert.Contains(director.Clip, IdleClips); Assert.Equal(CompanionEmoteKind.Idle, director.Kind);

        // A long line chains distinct talk clips, then the reaction at the line's end.
        director.Line(40, null, "dreamy", null, 20);
        plan = director.Pending;
        var talk = plan.Where(step => step.Kind == CompanionEmoteKind.Talk).ToList();
        Assert.InRange(talk.Count, 2, 4); Assert.Equal(talk.OrderBy(step => step.At), talk);
        Assert.All(talk.Zip(talk.Skip(1)), pair => Assert.NotEqual(pair.First.Clip, pair.Second.Clip));
        Assert.Equal(new CompanionEmoteStep(59.5, "entrancing", CompanionEmoteKind.Reaction), plan[^1]);
        Assert.All(talk, step => Assert.True(step.At < 59.5));
    }

    [Fact]
    public void ReactionsResolveByLineStemThenMood()
    {
        var director = new CompanionEmoteDirector(Map, new Random(1));
        Assert.Equal("praise", director.ResolveReaction("video_check_pass_2", null));
        Assert.Equal("tease", director.ResolveReaction("video_fail_1", "soft"));
        Assert.Equal("greeting", director.ResolveReaction("Session_Start_A", null));
        Assert.Equal("sultry", director.ResolveReaction("kw_relax", null));
        Assert.Equal("tease", director.ResolveReaction(null, "teasing, light"));
        Assert.Equal("tender", director.ResolveReaction("unknown_line", "Soft"));
        Assert.Null(director.ResolveReaction("unknown_line", "unknown"));
        Assert.Null(director.ResolveReaction(null, null));
        // No situational reaction: a random expressive clip, so repeated lines do not all look the same.
        director.Start(0); director.Line(10, "unknown_line", null, "Let it all slip away now", 6);
        Assert.Contains(director.Pending[^1].Clip, Map.Expressive);
    }

    [Fact]
    public void TriggerWordsAndNonVerbalSoundsNeverTalk()
    {
        var director = new CompanionEmoteDirector(Map, new Random(11));
        director.Start(0);
        // A single trigger word from idle: one expressive clip, no mouth movement.
        Assert.True(director.TriggerWord(5));
        var step = Assert.Single(director.Pending);
        Assert.Equal(CompanionEmoteKind.Reaction, step.Kind); Assert.Contains(step.Clip, Map.Expressive); Assert.Equal(5, step.At);
        Assert.False(director.IsSpeaking);
        // A reaction already playing keeps the avatar: a burst of trigger words does not restart it.
        director.Step(5); Assert.Equal(step.Clip, director.Clip);
        Assert.False(director.TriggerWord(6)); Assert.Empty(director.Pending);
        for (var now = 6d; now < 60; now += .5) { director.TriggerWord(now); director.Step(now); Assert.NotEqual(CompanionEmoteKind.Talk, director.Kind); }

        // Non-verbal text, an untexted short sound and a line too short for a talk window play one clip.
        foreach (var (text, seconds) in new (string?, double)[] { ("*giggles*", 2), ("mmm... ahh", 3), (null, 1.2), ("Relax", 1.2) })
        {
            director.Line(100, "kw_relax", null, text, seconds);
            var only = Assert.Single(director.Pending);
            Assert.Equal(CompanionEmoteKind.Reaction, only.Kind); Assert.False(director.IsSpeaking);
        }
        director.NonVerbal(200);
        Assert.Contains(Assert.Single(director.Pending).Clip, Map.Expressive);
        Assert.True(director.IsNonverbal(null, 1.4)); Assert.False(director.IsNonverbal(null, 1.5));
        Assert.False(director.IsNonverbal("giggle I love being here", 3));

        // A trigger word during a spoken line leaves the line alone.
        director.Start(300); director.Line(310, null, null, "Sink down and let every thought drift away", 8);
        var line = director.Pending.ToList();
        Assert.False(director.TriggerWord(310.5)); Assert.Equal(line, director.Pending);
    }

    [Fact]
    public void AClickPicksAClickEmoteAfterTheMinimumHoldWithACooldown()
    {
        var director = new CompanionEmoteDirector(Map, new Random(2));
        director.Start(0);
        Assert.True(director.Click(1));
        var click = Assert.Single(director.Pending);
        Assert.Contains(click.Clip, Map.ClickEmotes); Assert.NotEqual(director.Clip, click.Clip);
        Assert.Equal(2, click.At); Assert.Equal(CompanionEmoteKind.Reaction, click.Kind);
        Assert.False(director.Click(3.9)); Assert.True(director.Click(4));
        // A click interrupts a spoken line.
        director.Step(20); director.Line(20, null, null, "Listen to my voice and let go", 9);
        Assert.True(director.IsSpeaking);
        Assert.True(director.Click(30)); Assert.Single(director.Pending); Assert.False(director.IsSpeaking);
    }

    [Fact]
    public void AStopReturnsToTheIdleRotationAtOnce()
    {
        var director = new CompanionEmoteDirector(Map, new Random(4));
        director.Start(0);
        director.Line(10, null, null, "Breathe in slowly and let it all go now", 8);
        director.Step(11); Assert.Equal(CompanionEmoteKind.Talk, director.Kind);
        director.Stop(12);
        Assert.Empty(director.Pending); Assert.Equal(CompanionEmoteKind.Idle, director.Kind);
        Assert.Contains(director.Clip, IdleClips); Assert.Equal(12, director.ClipStarted);
        // A hidden tube that returns much later starts the next idle clip afresh.
        director.Step(500); Assert.Equal(500, director.ClipStarted);
    }

    internal static CompanionEmoteMap LoadMap()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "CCP.Classic.slnx"))) root = root.Parent;
        var folder = Path.Combine(root.FullName, "src", "CCP.Classic.App", "Assets", "Companion");
        return CompanionEmoteMap.Parse(File.ReadAllText(Path.Combine(folder, "emotes.json")), File.ReadAllText(Path.Combine(folder, "clip_timing.json")));
    }
}
