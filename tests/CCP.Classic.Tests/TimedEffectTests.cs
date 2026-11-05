using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class TimedEffectTests
{
    [Fact]
    public void PresetsPersistIndependentTimedChanges()
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-timed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new ClassicSettings { TimedChanges = [new(300, "spiral", true)] };
            var store = new PresetStore(folder); store.Save(PresetStore.Capture("Timed", settings));
            settings.TimedChanges.Clear();
            var saved = Assert.Single(store.LoadAll(out var recovered)); Assert.False(recovered);
            PresetStore.Apply(saved, settings); saved.TimedChanges.Clear();
            Assert.Equal(new TimedEffectChange(300, "spiral", true), Assert.Single(settings.TimedChanges));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ScheduleIsCapturedPerRunAndEachChangeRunsOnceAtItsTime()
    {
        var changes = new List<TimedEffectChange> { new(300, "spiral", true), new(600, "spiral", false) };
        var timeline = new TimedEffectTimeline(); timeline.Start(changes); changes.Clear();
        Assert.Empty(timeline.Step(299));
        Assert.Equal(new TimedEffectChange(300, "spiral", true), Assert.Single(timeline.Step(300)));
        Assert.Empty(timeline.Step(300)); Assert.Empty(timeline.Step(50));
        Assert.Equal(new TimedEffectChange(600, "spiral", false), Assert.Single(timeline.Step(900)));
        Assert.Empty(timeline.Step(1000));
        timeline.Stop(); Assert.Empty(timeline.Step(2000));
    }

    [Fact]
    public void NormalizationBoundsTheScheduleAndSkipsInvalidEntries()
    {
        var changes = new[] { new TimedEffectChange(double.NaN, "spiral", true), new(-1, "spiral", true),
            new(1, "unknown", true), new(86401, "spiral", true), new(30, "pink", true), new(0, "sub", false) };
        Assert.Equal(new[] { "sub", "pink" }, TimedEffectChange.Normalize(changes).Select(change => change.EffectId));
        Assert.Equal(Enumerable.Range(0, 64).Select(index => new TimedEffectChange(index, "pink", true)),
            TimedEffectChange.Normalize(Enumerable.Range(0, 100).Select(index => new TimedEffectChange(index, "pink", true))));
    }

    [Fact]
    public void RestartDoesNotReplayOldChangesOrAcceptInvalidTime()
    {
        var timeline = new TimedEffectTimeline(); timeline.Start([new(10, "video", true)]);
        Assert.Empty(timeline.Step(double.PositiveInfinity));
        timeline.Stop(); timeline.Start([new(0, "pink", true)]);
        Assert.Equal("pink", Assert.Single(timeline.Step(0)).EffectId);
        Assert.Empty(timeline.Step(100));
    }
}
