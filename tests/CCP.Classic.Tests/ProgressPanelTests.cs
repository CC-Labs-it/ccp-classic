using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ProgressPanelTests
{
    [Fact]
    public Task StatsXpAndBadgesUpdateWhileThePanelIsVisible() => UiDispatcher.Run(() =>
    {
        var clock = new ManualClock();
        var tracker = new ProgressTracker(new ProgressData { BubblesPopped = 999 }, clock);
        var panel = new ProgressPanel(tracker, new()); var window = Window(panel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("LVL 1", Named<TextBlock>(panel, "ProgressLevel").Text);
            Assert.Equal("○", Named<TextBlock>(panel, "AchievementMark_17").Text);
            tracker.StartSession("Daily"); clock.Advance(65); tracker.Tick(false, false);
            tracker.BubblePopped(); tracker.VideoFinished(20); tracker.AddXp(800);
            Assert.Equal("LVL 2", Named<TextBlock>(panel, "ProgressLevel").Text);
            Assert.Equal("00:01:05", Named<TextBlock>(panel, "ProgressStat_SessionTime").Text);
            Assert.Equal("1", Named<TextBlock>(panel, "ProgressStat_VideosFinished").Text);
            Assert.Equal("1,000", Named<TextBlock>(panel, "ProgressStat_BubblesPopped").Text);
            Assert.Equal("✓", Named<TextBlock>(panel, "AchievementMark_17").Text);
            Assert.Equal("Achievements 1 / 27", Named<TextBlock>(panel, "ProgressAchievementCount").Text);
            Assert.Equal(0, Named<ProgressBar>(panel, "ProgressXpBar").Value);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public Task EveryLanguageShowsAllTwentySevenNamesAndRequirementsWithoutRawKeys() => UiDispatcher.Run(() =>
    {
        var text = new LocalizationService(); var tracker = new ProgressTracker();
        var panel = new ProgressPanel(tracker, text); var window = Window(panel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var language in LocalizationService.Languages)
            {
                text.SetLanguage(language.Code);
                for (var index = 1; index <= 27; index++)
                {
                    var suffix = index.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                    var name = Named<TextBlock>(panel, "AchievementName_" + suffix).Text;
                    var requirement = Named<TextBlock>(panel, "AchievementRequirement_" + suffix).Text;
                    Assert.Equal(text["Achievement" + suffix], name);
                    Assert.Equal(text["AchievementRequirement" + suffix], requirement);
                    Assert.False(string.IsNullOrWhiteSpace(name)); Assert.False(string.IsNullOrWhiteSpace(requirement));
                    Assert.DoesNotContain("Achievement", name, StringComparison.Ordinal);
                    Assert.DoesNotContain("AchievementRequirement", requirement, StringComparison.Ordinal);
                }
                Assert.StartsWith(text["Level"] + " ", Named<TextBlock>(panel, "ProgressLevel").Text);
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public Task RemovingAndReattachingThePanelStopsAndRestoresItsLiveSubscription() => UiDispatcher.Run(() =>
    {
        var tracker = new ProgressTracker(); var panel = new ProgressPanel(tracker, new()); var window = Window(panel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var label = Named<TextBlock>(panel, "ProgressLevel");
            var host = Assert.IsType<ScrollViewer>(window.Content);
            host.Content = null; Dispatcher.UIThread.RunJobs();
            tracker.AddXp(800); Assert.Equal("LVL 1", label.Text);
            host.Content = panel; Dispatcher.UIThread.RunJobs(); Assert.Equal("LVL 2", label.Text);
            tracker.AddXp(ProgressTracker.GetXpForLevel(2)); Assert.Equal("LVL 3", label.Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static Window Window(ProgressPanel panel) => new()
    {
        Width = 700, Height = 650, Content = new ScrollViewer { Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
    };
    private static T Named<T>(ProgressPanel panel, string name) where T : Control =>
        Assert.Single(panel.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
