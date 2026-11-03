using System.Text.Json;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class KeywordParityTests
{
    private static ClassicSettings Settings(params KeywordDefinition[] triggers)
    {
        var settings = new ClassicSettings { KeywordTriggersEnabled = true };
        settings.Keywords = new() { GlobalCooldownSeconds = 0, SameWordCooldownSeconds = 0, LoopProtection = false, Triggers = triggers.ToList() };
        return settings;
    }
    [Fact]
    public void WholeWordPhrasesRegexAndMultipleActionsUseOneConsumedBuffer()
    {
        var plain = new KeywordDefinition { Keyword = "take a breath", Actions = [new(), new() { Kind = KeywordActionKind.Haptic }] };
        var regex = new KeywordDefinition { Keyword = "breath$", Match = KeywordMatch.Regex };
        var settings = Settings(plain, regex); var timeline = new KeywordTriggerTimeline(settings); timeline.Start();
        timeline.FeedText("take  a\tbreath"); Assert.Equal(3, timeline.Step(0).Count);
        timeline.FeedText(" "); Assert.Empty(timeline.Step(0));
        timeline.Step(31); timeline.FeedText("breathtaking"); Assert.Empty(timeline.Step(31));
    }
    [Fact]
    public void TimeoutContextResetDisabledAndStopDiscardInputAndPendingActions()
    {
        var settings = Settings(new KeywordDefinition { Keyword = "focus" }); var timeline = new KeywordTriggerTimeline(settings); timeline.Start();
        timeline.FeedText("fo"); timeline.Step(4); timeline.FeedText("cus"); Assert.Empty(timeline.Step(4));
        timeline.FeedText("\u001bfocus"); settings.KeywordTriggersEnabled = false; Assert.Empty(timeline.Step(5));
        settings.KeywordTriggersEnabled = true; timeline.Stop(); timeline.FeedText("focus"); Assert.Empty(timeline.Step(6));
        timeline.Start(); timeline.FeedText("focus"); Assert.Single(timeline.Step(0));
    }
    [Fact]
    public void GlobalPerDefinitionAndSameWordCooldownsRemainIndependent()
    {
        var trigger = new KeywordDefinition { Keyword = "focus", CooldownSeconds = 2 };
        var settings = Settings(trigger); settings.Keywords.GlobalCooldownSeconds = 1; settings.Keywords.SameWordCooldownSeconds = 4;
        var timeline = new KeywordTriggerTimeline(settings); timeline.Start();
        timeline.FeedText("focus"); Assert.Single(timeline.Step(0));
        timeline.Step(3); timeline.FeedText("focus"); Assert.Empty(timeline.Step(3));
        timeline.FeedText("\u001b"); timeline.Step(4); timeline.FeedText("focus"); Assert.Single(timeline.Step(4));
    }
    [Fact]
    public void AppScopeAndInvalidRegexDoNotFireOrThrow()
    {
        var settings = Settings(new KeywordDefinition { Keyword = "[", Match = KeywordMatch.Regex },
            new() { Keyword = "focus", AppScope = KeywordAppScope.OnlyListed, Apps = ["Editor.exe"] });
        var timeline = new KeywordTriggerTimeline(settings); timeline.Start();
        timeline.FeedText("focus", "browser"); Assert.Empty(timeline.Step(0));
        timeline.FeedText("\u001bfocus", "Editor"); Assert.Single(timeline.Step(0));
    }
    [Fact]
    public void ScopedInputCannotCombineAcrossAppsAndLoopMuteUsesTheLongerWindow()
    {
        var settings = Settings(new KeywordDefinition { Keyword = "focus", AppScope = KeywordAppScope.OnlyListed,
            Apps = ["editor"], CooldownSeconds = 1 });
        settings.Keywords.LoopProtection = true; settings.Keywords.LoopProtectionMs = 5000;
        var timeline = new KeywordTriggerTimeline(settings); timeline.Start();
        timeline.FeedText("fo", "browser"); timeline.FeedText("cus", "editor"); Assert.Empty(timeline.Step(0));
        timeline.Clear(); timeline.FeedText("focus", "editor"); Assert.Single(timeline.Step(0));
        timeline.Step(2); timeline.FeedText("focus", "editor"); Assert.Empty(timeline.Step(2));
        timeline.Clear(); timeline.Step(5); timeline.FeedText("focus", "editor"); Assert.Single(timeline.Step(5));
    }
    [Fact]
    public void ImportingPhrasePoolAddsLinkedLocalAudioAndNeverDuplicatesWords()
    {
        var options = new KeywordOptions();
        var path = Path.Combine(Path.GetTempPath(), "custom.wav");
        Assert.Equal(2, options.ImportPhrases([new("relax"), new("Custom", path), new("No audio", "https://invalid/clip.mp3")]));
        Assert.Equal(0, options.ImportPhrases([new("CUSTOM", path)]));
        var custom = options.Triggers.Single(t => t.Keyword == "Custom");
        Assert.Equal(path, custom.Actions.Single(a => a.Kind == KeywordActionKind.Audio).AudioPath);
        Assert.Contains(custom.Actions, a => a.Kind == KeywordActionKind.Haptic && a.Gain == .5);
        Assert.DoesNotContain(options.Triggers.Single(t => t.Keyword == "No audio").Actions, a => a.Kind == KeywordActionKind.Audio);
    }
    [Fact]
    public void TypedDefinitionsRoundTripAndNormalizeLocalAudioWithoutNetworkInputs()
    {
        var settings = Settings(new KeywordDefinition { Keyword = "custom", Actions = [new() { AudioPath = "https://example.test/a.mp3", Gain = double.NaN }] });
        settings.Normalize(); Assert.Null(settings.Keywords.Triggers[0].Actions[0].AudioPath);
        var copy = JsonSerializer.Deserialize<ClassicSettings>(JsonSerializer.Serialize(settings))!; copy.Normalize();
        Assert.Equal("custom", copy.Keywords.Triggers[0].Keyword); Assert.Equal(.8, copy.Keywords.Triggers[0].Actions[0].Gain);
    }
    [Fact]
    public void LockMatchingAcceptsKeyboardPunctuationAndKeepsWrongTextUnmatched()
    {
        Assert.True(LockCardText.Matches("  take a breath... ", "Take\u00a0a breath\u2026"));
        Assert.True(LockCardText.Matches("'focus' - now", "\u2018focus\u2019 \u2014 now"));
        Assert.True(LockCardText.IsPrefixOf("take a breath.", "Take a breath\u2026"));
        Assert.True(LockCardText.Matches("take a breath", "take a breath..."));
        Assert.Equal(16, LockCardText.TypedLength("take a breath..."));
    }
    [Fact]
    public void TargetLengthRandomRepeatsAndManualLockWorkWhileScheduleIsOff()
    {
        var settings = new ClassicSettings(); settings.LockCard.Phrases = new() { ["Custom phrase"] = true };
        settings.LockCard.TargetLengthEnabled = true; settings.LockCard.TargetLength = 120; settings.LockCard.TargetVariance = 0;
        var timeline = new LockEffectsTimeline(settings); timeline.Start(); Assert.True(timeline.TriggerLock());
        var cue = timeline.Step(0).LockCard!; Assert.Equal(10, cue.Repetitions); Assert.False(settings.Effects["lock"].Enabled);
        settings.LockCard.ResetOnTypo = true; timeline.SubmitLock(cue.Id, "custom phrase"); timeline.RegisterTypo(cue.Id);
        Assert.Equal(0, timeline.Step(1).LockCard!.Completed);
        timeline.Stop(); Assert.Null(timeline.Step(2).LockCard);
        settings.LockCard.TargetLengthEnabled = false; settings.LockCard.RandomRepeats = true; settings.LockCard.MinimumRepeats = 2;
        Assert.Equal(2, settings.LockCard.ResolveRepeats("x", 5, 0)); Assert.Equal(5, settings.LockCard.ResolveRepeats("x", 5, .99999));
    }
    [Fact]
    public void LegacyTimingAndTextSizeMigrateWithoutChangingUnrelatedChoices()
    {
        var old = JsonSerializer.Deserialize<ClassicSettings>("""{"SchemaVersion":1,"Effects":{"lock":{"Values":{"interval":30}},"wipe":{"Values":{"interval":45}},"bounce":{"Values":{"size":40}}}}""")!;
        old.Normalize(); Assert.Equal(2, old.Effects["lock"].Values["rate"]);
        Assert.Equal(60d / 45, old.Effects["wipe"].Values["rate"]); Assert.Equal(100, old.Effects["bounce"].Values["size"]);
        old.Normalize(); Assert.Equal(100, old.Effects["bounce"].Values["size"]);
    }

    [Fact]
    public void PresetsKeepCustomizationWithoutAliasingOrEnablingKeyboardListening()
    {
        var settings = new ClassicSettings(); settings.LockCard.Phrases = new() { ["My phrase"] = true };
        settings.BouncingText.FontFamily = "Fredoka"; settings.Bubbles.Motion = BubbleMotion.Rain;
        var preset = PresetStore.Capture("Custom", settings); settings.LockCard.Phrases.Clear();
        PresetStore.Apply(preset, settings); Assert.True(settings.LockCard.Phrases["My phrase"]);
        Assert.Equal("Fredoka", settings.BouncingText.FontFamily); Assert.Equal(BubbleMotion.Rain, settings.Bubbles.Motion);
        Assert.False(settings.KeywordTriggersEnabled); settings.LockCard.Phrases.Clear();
        Assert.Single(preset.Customization!.LockCard.Phrases);
    }

    [Fact]
    public void BubbleMotionKeepsOriginalSizeRailsAndSpiralTurnsAtTheCore()
    {
        var settings = new ClassicSettings(); settings.Bubbles.Motion = BubbleMotion.Rain;
        settings.Effects["bubble"].Enabled = false;
        var timeline = new InteractiveEffectsTimeline(settings); timeline.Start(); Assert.True(timeline.TriggerBubbles(1));
        var top = Assert.Single(timeline.Step(0).Bubbles); Assert.InRange(top.DiameterDip, 150, 249);
        var next = Assert.Single(timeline.Step(.1).Bubbles); Assert.True(next.Y > top.Y);
        var radius = 900 * .42; var frames = (radius - 24) / .55;
        var core = BubbleSpiralPath.Sample(radius, 0, 1, frames, 1, false);
        Assert.Equal(24, core.Radius, 8); Assert.Equal(Math.Tau * 2.5, core.Angle, 8); Assert.Equal(0, core.Opacity);
    }
}
