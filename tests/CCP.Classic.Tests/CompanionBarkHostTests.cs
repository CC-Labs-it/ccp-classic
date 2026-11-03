using Avalonia.Controls;
using Avalonia.VisualTree;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class CompanionBarkHostTests
{
    [Fact]
    public async Task TheIdleTubeAnnouncesWhenItIsOnScreenAndShowsLibraryLines()
    {
        Window owner = null!; CompanionHost host = null!; var clock = new ManualClock();
        var settings = new ClassicSettings { BarkIntervalSeconds = 6, GentleVisuals = true };
        var line = BarkLibrary.Default.For("SessionStarted").SelectMany(rule => rule.Lines).First(item => item.Text.Length > 24);
        var timeline = new CompanionTimeline(settings);
        var started = 0; var spoken = new List<CompanionBarkCue>();
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                host = new(owner, settings, window => new(window, settings, _ => { }, requireTransparency: false), clock, timeline);
                host.IdleStarted += () => started++; host.IdleBark += spoken.Add; host.Refresh();
            });
            var shown = false;
            for (var attempt = 0; attempt < 100 && !shown; attempt++)
            {
                await UiDispatcher.Run(() => { host.IdleTick(); shown = host.Window!.DecodedFrames > 0 && host.Window.IsVisible; });
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Assert.True(shown);
            await UiDispatcher.Run(() =>
            {
                // Once per appearance, not once per idle frame.
                Assert.Equal(1, started); host.IdleTick(); host.IdleTick(); Assert.Equal(1, started);
                var pick = new BarkPick(BarkLibrary.Default.For("SessionStarted")[0], line);
                Assert.True(timeline.Say(pick, host.Now)); host.IdleTick();
                var words = host.Window!.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CompanionBark");
                Assert.Equal(line.Text, words.Text); Assert.True(words.FontSize < 26);
                Assert.Equal(line.Text, Assert.Single(spoken).Text);
                owner.WindowState = WindowState.Minimized; host.IdleTick();
                owner.WindowState = WindowState.Normal; owner.Activate(); host.IdleTick();
                Assert.Equal(2, started);
            });
        }
        finally { await UiDispatcher.Run(() => { host?.Dispose(); owner?.Close(); }); }
    }

    // Time stands still: idle words never interrupt the check.
    private sealed class ManualClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
    }
}
