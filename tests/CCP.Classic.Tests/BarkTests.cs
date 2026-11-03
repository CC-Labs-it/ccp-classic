using System.Text.RegularExpressions;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class BarkTests
{
    // Every event key the app raises (docs/BARKS.md). A rule under any other key could never speak.
    private static readonly HashSet<string> Raised =
    [
        "AppOpened", "StreakMilestone", "SessionStarted", "SessionMinutes", "SessionCompleted", "SessionRefocused", "SetupReady",
        "Flash", "Subliminal", "VideoStarted", "VideoEnded", "AttentionPass", "AttentionFail", "BubblePopped", "BouncingTextCorner",
        "LockCardCompleted", "MindWipe", "BrainDrain", "KeywordTriggered", "PreviewLockCard", "PreviewVideo",
        "LevelUp", "Achievement", "AvatarClicked", "Idle", "Detached", "BarksOn", "PanicOff",
        "Opened.Presets", "Opened.Studio", "Opened.Progress", "Opened.Settings", "Opened.Toys", "Opened.Library",
        .. new[] { "flash", "video", "sub", "spiral", "pink", "bubble", "lock", "bounce", "wipe", "drain" }.Select(id => "Opened.Effect." + id),
        .. new[] { "sub", "spiral", "pink", "drain", "bubble", "lock", "bounce", "wipe", "wall", "strict", "keywords", "allmonitors", "startup", "autorun" }
            .Select(id => "Enabled." + id)
    ];

    [Fact]
    public void EveryShippedLineHasItsRecordingAndEveryRecordingIsUsed()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Starter", "Barks");
        var clips = BarkLibrary.Default.Rules.SelectMany(rule => rule.Lines).Select(line => line.Clip).ToHashSet();
        Assert.True(clips.Count > 250);
        foreach (var clip in clips)
        {
            var file = new FileInfo(Path.Combine(folder, clip));
            Assert.True(file.Exists, clip); Assert.True(file.Length > 1024, clip + " is an LFS pointer");
        }
        Assert.Equal(clips.OrderBy(name => name), Directory.GetFiles(folder, "*.mp3").Select(Path.GetFileName).OrderBy(name => name));
        Assert.All(BarkLibrary.Default.Rules.SelectMany(rule => rule.Lines), line => Assert.InRange(line.Seconds, .3, 20));
    }

    [Fact]
    public void KeptLinesAreNeutralAndOffline()
    {
        var banned = new Regex(@"\b(girl|boy|sissy|bimbo|bambi|princess|chaster|discord|webcam|ai|mod|mods|she|he|her|him|his|patreon|phone)\b",
            RegexOptions.IgnoreCase);
        foreach (var line in BarkLibrary.Default.Rules.SelectMany(rule => rule.Lines))
        {
            Assert.False(banned.IsMatch(line.Text), line.Text);
            Assert.DoesNotContain((char)0x2014, line.Text); Assert.DoesNotContain((char)0x2013, line.Text);
            Assert.InRange(line.Text.Length, 2, 160);
        }
    }

    [Fact]
    public void EveryRuleListensToAnEventTheAppRaises()
    {
        var triggers = BarkLibrary.Default.Rules.Select(rule => rule.Trigger).ToHashSet();
        Assert.Empty(triggers.Except(Raised));
        Assert.Empty(Raised.Except(triggers));
        Assert.True(BarkLibrary.Default.For("Idle").Sum(rule => rule.Lines.Count) > 100);
        Assert.True(CompanionTimeline.IsApproved("Focus"));
        Assert.True(CompanionTimeline.IsApproved(BarkLibrary.Default.For("Flash")[0].Lines[0].Text));
        Assert.False(CompanionTimeline.IsApproved("Unapproved"));
    }

    [Fact]
    public void ConditionsAndPriorityPickTheMilestoneLine()
    {
        var now = 0d; var engine = new BarkEngine(BarkLibrary.Default, () => now, () => 0);
        Assert.Equal("ms_level_10", engine.Raise("LevelUp", Facts(("level", 10)))!.Rule.Id);
        now += 1;
        Assert.Equal("level_up", engine.Raise("LevelUp", Facts(("level", 11)))!.Rule.Id);
        now += 1;
        Assert.Equal("lockcard_clean", engine.Raise("LockCardCompleted", Facts(("mistakes", 0)))!.Rule.Id);
        now += 1;
        Assert.Equal("lockcard_sloppy", engine.Raise("LockCardCompleted", Facts(("mistakes", 3)))!.Rule.Id);
        // A missing fact never satisfies a condition.
        Assert.Null(engine.Raise("SessionMinutes"));
        Assert.Null(engine.Raise("NoSuchEvent"));
    }

    [Fact]
    public void CooldownGapAndAlwaysRules()
    {
        var now = 0d;
        var engine = new BarkEngine(Library(Rule("chat", "Chat", cooldown: 10, lines: 3), Rule("click", "Click", always: true, lines: 3)),
            () => now, () => 0) { MinimumGapSeconds = 5 };
        Assert.NotNull(engine.Raise("Chat"));
        now = 4; Assert.Null(engine.Raise("Chat"));
        // A direct answer skips the global gap.
        Assert.NotNull(engine.Raise("Click"));
        now = 9.5; Assert.Null(engine.Raise("Chat")); // own cooldown from 0
        now = 10; Assert.NotNull(engine.Raise("Chat"));
        // Busy: a line is showing; only an Always rule replaces it.
        now = 30; Assert.Null(engine.Raise("Chat", busy: true)); Assert.NotNull(engine.Raise("Click", busy: true));
    }

    [Fact]
    public void NoLineTwiceInARowAndAPoolRotatesBeforeRepeating()
    {
        var now = 0d; var roll = 0d;
        var engine = new BarkEngine(Library(Rule("idle", "Idle", lines: 4)), () => now += 1, () => roll) { MinimumGapSeconds = 0 };
        var spoken = new List<string>();
        for (var i = 0; i < 12; i++) { roll = i % 3 / 3d; spoken.Add(engine.Raise("Idle")!.Line.Clip); }
        for (var i = 1; i < spoken.Count; i++) Assert.NotEqual(spoken[i - 1], spoken[i]);
        Assert.Equal(4, spoken.Take(4).Distinct().Count());
        Assert.Equal(4, spoken.Skip(4).Take(4).Distinct().Count());
        // A single-line rule must repeat; it still speaks.
        var single = new BarkEngine(Library(Rule("one", "One", lines: 1)), () => now += 1, () => 0) { MinimumGapSeconds = 0 };
        Assert.NotNull(single.Raise("One")); Assert.NotNull(single.Raise("One"));
    }

    [Fact]
    public void OncePerSessionRulesFallThroughAndReturnNextSession()
    {
        var now = 0d;
        var engine = new BarkEngine(Library(Rule("first", "Start", priority: 9, repeatable: false), Rule("later", "Start", priority: 1)),
            () => now += 1, () => 0) { MinimumGapSeconds = 0 };
        Assert.Equal("first", engine.Raise("Start")!.Rule.Id);
        Assert.Equal("later", engine.Raise("Start")!.Rule.Id);
        engine.BeginSession();
        Assert.Equal("first", engine.Raise("Start")!.Rule.Id);
    }

    [Fact]
    public void AFailedChanceFallsThroughToTheNextRule()
    {
        var now = 0d; var roll = .9;
        var engine = new BarkEngine(Library(Rule("rare", "Idle", priority: 9, chance: .35), Rule("common", "Idle")), () => now += 1, () => roll)
            { MinimumGapSeconds = 0 };
        Assert.Equal("common", engine.Raise("Idle")!.Rule.Id);
        roll = .1; Assert.Equal("rare", engine.Raise("Idle")!.Rule.Id);
    }

    [Fact]
    public void PanicSilencesEveryBarkUntilTheQuietEnds()
    {
        var now = 0d;
        var engine = new BarkEngine(Library(Rule("click", "Click", always: true), Rule("idle", "Idle")), () => now, () => 0) { MinimumGapSeconds = 0 };
        engine.Panic(); Assert.True(engine.IsQuiet);
        Assert.Null(engine.Raise("Click")); Assert.Null(engine.Raise("Idle"));
        now = BarkEngine.PanicQuietSeconds - .1; Assert.Null(engine.Raise("Click"));
        now = BarkEngine.PanicQuietSeconds; Assert.False(engine.IsQuiet); Assert.NotNull(engine.Raise("Click"));
        // Nothing spoken during the quiet was saved up.
        now += 1; Assert.NotNull(engine.Raise("Idle"));
    }

    [Fact]
    public void TheTimelineTakesIdleWordsAndClicksFromTheLibraryAndShowsEventLines()
    {
        var settings = new ClassicSettings { CompanionEnabled = true, BarksEnabled = true, BarkIntervalSeconds = 6 };
        var asked = new List<string>(); var engine = new BarkEngine(Library(Rule("idle", "Idle"), Rule("click", "AvatarClicked", always: true),
            Rule("flash", "Flash", seconds: 9)), () => 0, () => 0) { MinimumGapSeconds = 0 };
        var timeline = new CompanionTimeline(settings) { Lines = trigger => { asked.Add(trigger); return engine.Raise(trigger); } };
        timeline.StartIdle(100);
        var idle = timeline.Step(106).Bark!; Assert.Equal("idle", idle.Pick!.Rule.Id); Assert.Equal(-1, idle.AudioIndex);
        Assert.Empty(timeline.Step(107).Triggers);
        Assert.True(timeline.Poke(112)); Assert.Equal("click", timeline.Step(112).Bark!.Pick!.Rule.Id);
        Assert.Equal(new[] { "Idle", "AvatarClicked" }, asked);
        // An event line replaces the word, stays up while its clip plays, and pushes the timer back.
        var flash = engine.Raise("Flash")!; Assert.True(timeline.Say(flash, 113)); Assert.True(timeline.IsSpeaking);
        var shown = timeline.Step(113).Bark!; Assert.Equal(flash.Line.Text, shown.Text); Assert.Equal(9, shown.Seconds);
        Assert.NotNull(timeline.Step(122).Bark); Assert.Equal(new[] { "Idle", "AvatarClicked" }, asked);
        // Session words keep the approved five and their trigger phrases.
        timeline.Start(); var word = timeline.Step(6).Bark!; Assert.Equal("Breathe", word.Text); Assert.Null(word.Pick);
        Assert.Equal(2.72, word.Seconds);
        // Barks off: nothing is said.
        settings.BarksEnabled = false; Assert.False(timeline.Say(flash, 7));
    }

    [Fact]
    public void SwitchesTurnedOnRaiseTheirEventsButPresetsAndTimedChangesDoNot()
    {
        var settings = new ClassicSettings(); var panic = true;
        foreach (var effect in settings.Effects.Values) effect.Enabled = false;
        var watch = new BarkSwitchWatch(settings, () => panic);
        Assert.Empty(watch.Poll());
        settings.Effects["spiral"].Enabled = true; Assert.Equal(new[] { "Enabled.spiral" }, watch.Poll());
        settings.Effects["spiral"].Enabled = false; Assert.Empty(watch.Poll());
        panic = false; Assert.Equal(new[] { "PanicOff" }, watch.Poll());
        settings.BarksEnabled = false; watch.Poll(); settings.BarksEnabled = true; Assert.Equal(new[] { "BarksOn" }, watch.Poll());
        settings.CompanionPlacement.Detached = true; Assert.Equal(new[] { "Detached" }, watch.Poll());
        // A preset flips many switches at once: no single choice to comment on.
        foreach (var id in new[] { "flash", "sub", "pink", "bubble" }) settings.Effects[id].Enabled = true;
        Assert.Empty(watch.Poll());
        settings.Effects["wipe"].Enabled = true; watch.Rebase(); Assert.Empty(watch.Poll());
    }

    private static Dictionary<string, double> Facts(params (string Key, double Value)[] facts) =>
        facts.ToDictionary(fact => fact.Key, fact => fact.Value);
    private static BarkLibrary Library(params BarkRule[] rules) => new(rules);
    private static BarkRule Rule(string id, string trigger, int priority = 5, double cooldown = 0, bool repeatable = true, double chance = 1,
        bool always = false, int lines = 2, double seconds = 3) =>
        new(id, trigger, priority, cooldown, repeatable, chance, always, "", new Dictionary<string, BarkRange>(),
            Enumerable.Range(1, lines).Select(index => new BarkLine($"{id} line {index}", $"{id}_{index}.mp3", seconds)).ToArray());
}
