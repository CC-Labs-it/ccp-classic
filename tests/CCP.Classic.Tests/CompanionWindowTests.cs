using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class CompanionWindowTests
{
    [Fact]
    public Task OriginalTubeClipsMatchPublishedHashesAndDecodeWithTransparency() => UiDispatcher.Run(() =>
    {
        foreach (var (name, hash) in new[]
        {
            ("idle", "9A8A1707902C1392D827C8B987A5150DEB194818C33DD52FE50FBB0BFF6602AA"),
            ("talkA", "5588D0FF0A05A5FEE9934A0FD196A760F47EC4AEC8C8F8299E4D639CC7C85859"),
            ("greeting", "84CC43CBF520A38623D8D9BFCEA452A1BE6F269DD78EC022AE9D95F7CC9EAA3A")
        })
        {
            var uri = new Uri($"avares://CCP.Classic.App/Assets/Companion/{name}.gif");
            using (var original = AssetLoader.Open(uri)) Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(original)));
            using var source = AssetLoader.Open(uri); using var data = SKData.Create(source);
            using var codec = SKCodec.Create(data); Assert.NotNull(codec);
            Assert.Equal(120, codec.FrameCount); Assert.Equal(192, codec.Info.Width); Assert.Equal(309, codec.Info.Height);
            using var bitmap = new SKBitmap(new SKImageInfo(192, 309, SKColorType.Bgra8888, SKAlphaType.Premul));
            foreach (var frame in new[] { 0, 60, 119 })
            {
                Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(frame)));
                Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            }
        }
    });

    [Fact]
    public async Task NativeCompanionKeepsItsClipUnderMotionPreferencesAndHidesWhenItsOwnerMinimizes()
    {
        Window owner = null!; CompanionWindow companion = null!; ClassicSettings settings = new() { CompanionEnabled = true };
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new() { Position = new PixelPoint(800, 100) }; owner.Show(); owner.Activate();
                companion = new(owner, settings, _ => { }, requireTransparency: false); companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0, () => companion.Update(null, .1));
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, .2); Assert.True(companion.IsVisible); Assert.Same(owner, companion.Owner);
                Assert.True(companion.IsMotionActive);
                // The tube sits beside main with her art clear of it, and she bobs within four canvas units.
                Assert.Equal(TubeDockSide.Left, companion.DockSide);
                // The pipes overlap the panel's visible edge by DockOverlap; allow one more pixel of rounding.
                Assert.True(companion.Position.X + companion.Width * owner.DesktopScaling
                    <= owner.Position.X + (TubeLayout.DockOverlap + 1) * owner.DesktopScaling);
                var bobs = Enumerable.Range(0, 8).Select(step => { companion.Update(null, .25 + step * .25); return companion.BobOffset; }).ToArray();
                Assert.All(bobs, offset => Assert.InRange(offset, -4, 4)); Assert.Contains(bobs, offset => Math.Abs(offset) > 1);
                var docked = companion.Position;
                owner.Position = new PixelPoint(owner.Position.X + 40, owner.Position.Y + 30); Dispatcher.UIThread.RunJobs();
                Assert.Equal(new PixelPoint(docked.X + 40, docked.Y + 30), companion.Position);
                settings.GentleVisuals = true;
            });
            // Gentle visuals and Reduced motion still the bob only; the clip keeps decoding new frames.
            var time = await Advances(companion, 2);
            await UiDispatcher.Run(() =>
            {
                Assert.False(companion.IsMotionActive); Assert.True(companion.IsClipPlaying); Assert.Equal(0, companion.BobOffset);
                settings.GentleVisuals = false; settings.ReducedMotion = true;
            });
            time = await Advances(companion, time);
            await UiDispatcher.Run(() =>
            {
                Assert.False(companion.IsMotionActive); Assert.Equal(0, companion.BobOffset);
                settings.ReducedMotion = false; settings.PerformanceMode = true;
            });
            // Performance mode lowers the clip rate without freezing it.
            time = await Advances(companion, time);
            await UiDispatcher.Run(() =>
            {
                Assert.Equal(1d / 15, companion.FrameSeconds, 6); Assert.True(companion.IsClipPlaying);
                owner.WindowState = WindowState.Minimized; companion.Update(null, time += .1);
                Assert.False(companion.IsVisible); Assert.False(companion.IsClipPlaying);
                owner.WindowState = WindowState.Normal; settings.PerformanceMode = false; owner.Activate();
                companion.Update(null, time += .1); Assert.True(companion.IsVisible);
                Deactivate(owner); Assert.False(owner.IsActive);
            });
            // Another app in front: she keeps moving.
            await Advances(companion, time);
            await UiDispatcher.Run(() => { Assert.True(companion.IsClipPlaying); Assert.True(companion.IsMotionActive); });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    [Fact]
    public async Task OnlyApprovedBarksAppearAndClickCallbacksLeaveStopPolicyToTheOwner()
    {
        Window owner = null!; CompanionWindow companion = null!; var clicked = 0; var stops = 0;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, new() { CompanionEnabled = true }, _ => { }, requireTransparency: false);
                companion.AvatarClicked += () => clicked++; companion.StopRequested += () => stops++;
                companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0, () => companion.Update(null, .1));
            await UiDispatcher.Run(() =>
            {
                companion.Update(new(1, "Focus", 0, .5), .2); Dispatcher.UIThread.RunJobs();
                var words = companion.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CompanionBark");
                Assert.Equal("Focus", words.Text);
                companion.Update(new(2, "Unapproved", 0, 1), .2); Assert.Equal("", words.Text);
                var point = HitPoint(companion);
                companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left);
                // Right-click opens the tube menu (CCP parity): no poke and no stop of its own.
                companion.MouseDown(point, MouseButton.Right); companion.MouseUp(point, MouseButton.Right);
                Assert.Equal(1, clicked); Assert.Equal(0, stops); Assert.True(companion.Menu.IsOpen); Assert.True(companion.IsVisible);
                companion.Menu.Close();
                companion.Close(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, stops); Assert.True(companion.IsVisible);
                companion.FinishImmediately(); Assert.False(companion.IsVisible);
                Assert.All(Portraits(companion), image => Assert.Null(image.Source));
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("minimized")]
    public async Task ALateFirstFrameCannotPublishAfterItsPresentationIsSuppressed(string suppression)
    {
        Window owner = null!; CompanionWindow companion = null!;
        using var decoding = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var settings = new ClassicSettings { CompanionEnabled = true };
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, settings, _ => { }, requireTransparency: false,
                    beforeDecode: () => { decoding.Set(); release.Wait(TestContext.Current.CancellationToken); });
            });
            await Until(() => owner.IsActive, owner.Activate);
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, 0); Assert.True(companion.IsDecodePending);
            });
            Assert.True(decoding.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            await UiDispatcher.Run(() =>
            {
                if (suppression == "disabled") settings.CompanionEnabled = false;
                else if (suppression == "hidden") owner.Hide();
                else owner.WindowState = WindowState.Minimized;
                release.Set();
            });
            await Until(() => !companion.IsDecodePending, () => { });
            await UiDispatcher.Run(() =>
            {
                Assert.Equal(0, companion.DecodedFrames); Assert.False(companion.IsVisible);
                Assert.All(Portraits(companion), image => Assert.Null(image.Source));
                for (var index = 0; index < 100; index++)
                {
                    companion.Update(null, 1 + index * .1);
                    Assert.False(companion.IsDecodePending);
                }
                settings.CompanionEnabled = true; owner.WindowState = WindowState.Normal;
                if (!owner.IsVisible) owner.Show(); owner.Activate();
            });
            await Until(() => owner.IsActive, owner.Activate);
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, 12); Assert.True(companion.IsDecodePending);
            });
            await Until(() => companion.DecodedFrames > 0, () => companion.Update(null, 12.1));
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, 12.2); Assert.True(companion.IsVisible);
                Assert.Null(companion.LastError);
            });
        }
        finally { release.Set(); await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    // The tube is not tied to focus: another app in front still gets the first frame and a moving clip.
    [Fact]
    public async Task AnInactiveOwnerStillPublishesTheFirstFrameAndKeepsTheClipPlaying()
    {
        Window owner = null!; Window other = null!; CompanionWindow companion = null!;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); other = new(); other.Show(); other.Activate(); Dispatcher.UIThread.RunJobs();
                Deactivate(owner); Assert.False(owner.IsActive);
                companion = new(owner, new() { CompanionEnabled = true, GentleVisuals = true }, _ => { }, requireTransparency: false);
                companion.Update(null, 0); Assert.True(companion.IsDecodePending);
            });
            await Until(() => companion.DecodedFrames > 0 && companion.IsVisible, () => companion.Update(null, .1));
            await Advances(companion, .2);
            await UiDispatcher.Run(() => { Assert.False(owner.IsActive); Assert.Equal(0, companion.BobOffset); });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); other?.Close(); owner?.Close(); }); }
    }

    [Fact]
    public async Task ForcedCloseDiscardsThePendingFrameAndQueuedStop()
    {
        Window owner = null!; CompanionWindow companion = null!; var stops = 0;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, new() { CompanionEnabled = true }, _ => { }, requireTransparency: false);
                companion.StopRequested += () => stops++;
            });
            await Until(() => owner.IsActive, owner.Activate);
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, 0); Assert.True(companion.IsDecodePending);
                companion.Show(owner); companion.Close(); companion.FinishImmediately();
            });
            await Until(() => !companion.IsDecodePending, () => { });
            await UiDispatcher.Run(() =>
            {
                Dispatcher.UIThread.RunJobs(); Assert.Equal(0, stops); Assert.Equal(0, companion.DecodedFrames);
                Assert.False(companion.IsVisible);
                Assert.All(Portraits(companion), image => Assert.Null(image.Source));
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    [Fact]
    public async Task TheTubeIdlesWithoutASessionAndAStopEndsBarksButKeepsIt()
    {
        Window owner = null!; CompanionHost host = null!; var settings = new ClassicSettings(); var stops = 0;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                host = new(owner, settings, window => new(window, settings, _ => { }, requireTransparency: false));
                host.StopRequested += () => stops++;
                Assert.True(settings.CompanionEnabled); host.Refresh();
                Assert.NotNull(host.Window); Assert.True(host.IdleRunning);
            });
            await Until(() => host.Window!.DecodedFrames > 0 && host.Window.IsVisible, host.IdleTick);
            await UiDispatcher.Run(() =>
            {
                var companion = host.Window!;
                var words = companion.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CompanionBark");
                host.BeginSession(); Assert.False(host.IdleRunning);
                host.SessionFrame(new(1, "Focus", 0, 1)); Assert.Equal("Focus", words.Text);
                // Closing the tube still asks the owner to stop; the stop itself is the owner's panic path.
                // Right-click opens the menu, whose Stop also goes to the owner (CompanionMenuTests).
                var point = HitPoint(companion);
                companion.MouseDown(point, MouseButton.Right); companion.MouseUp(point, MouseButton.Right);
                Assert.Equal(0, stops); Assert.True(companion.Menu.IsOpen); companion.Menu.Close();
                companion.Close(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, stops);
                host.EndSession();
                Assert.Same(companion, host.Window); Assert.True(companion.IsVisible);
                Assert.Equal("", words.Text); Assert.True(host.IdleRunning);
                settings.CompanionEnabled = false; host.Refresh();
                Assert.Null(host.Window); Assert.False(companion.IsVisible); Assert.False(host.IdleRunning);
                Assert.All(Portraits(companion), image => Assert.Null(image.Source));
                settings.CompanionEnabled = true; host.Refresh(); Assert.NotNull(host.Window); Assert.NotSame(companion, host.Window);
                host.Dispose(); Assert.Null(host.Window); Assert.Equal(1, stops);
            });
        }
        finally { await UiDispatcher.Run(() => { host?.Dispose(); owner?.Close(); }); }
    }

    [Fact]
    public async Task IdleBarksSpeakOnTheIntervalAndAClickAnswersAtOnce()
    {
        Window owner = null!; CompanionHost host = null!; var clock = new ManualClock();
        var settings = new ClassicSettings { BarkIntervalSeconds = 6, GentleVisuals = true, TriggerPhrasesEnabled = true };
        var spoken = new List<CompanionBarkCue>(); var clicks = 0;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                Assert.True(settings.BarksEnabled);
                host = new(owner, settings, window => new(window, settings, _ => { }, requireTransparency: false), clock);
                host.IdleBark += spoken.Add; host.AvatarClicked += () => clicks++; host.Refresh();
            });
            await Until(() => host.Window!.DecodedFrames > 0 && host.Window.IsVisible, host.IdleTick);
            await UiDispatcher.Run(() =>
            {
                var companion = host.Window!;
                var words = companion.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CompanionBark");
                // No session runs: the first word comes one interval after the tube appears.
                clock.Advance(5.9); host.IdleTick(); Assert.Empty(spoken); Assert.Equal("", words.Text);
                clock.Advance(.1); host.IdleTick();
                Assert.Equal("Breathe", Assert.Single(spoken).Text); Assert.Equal("Breathe", words.Text);
                // A left click answers at once; a second click inside the cooldown does not stack another word.
                clock.Advance(1); var point = HitPoint(companion);
                companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, clicks); Assert.Equal(new[] { "Breathe", "Relax" }, spoken.Select(bark => bark.Text));
                Assert.Equal("Relax", words.Text); Assert.Equal("talkA", companion.WantedClip);
                clock.Advance(.5); companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left);
                Assert.Equal(2, clicks); Assert.Equal(2, spoken.Count);
                // A stop silences the word at once; the next one waits a full interval instead of firing straight away.
                host.EndSession(); Assert.Equal("", words.Text);
                clock.Advance(5.9); host.IdleTick(); Assert.Equal(2, spoken.Count);
                clock.Advance(.1); host.IdleTick(); Assert.Equal(3, spoken.Count);
                // A minimised panel says nothing and does not save words up for its return.
                owner.WindowState = WindowState.Minimized; clock.Advance(60); host.IdleTick(); Assert.Equal(3, spoken.Count);
                owner.WindowState = WindowState.Normal; owner.Activate(); host.IdleTick(); Assert.Equal(3, spoken.Count);
                // Barks off: a click still plays the greeting, with no word.
                settings.BarksEnabled = false; clock.Advance(2); host.IdleTick(); clock.Advance(1); host.IdleTick();
                companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left); host.IdleTick();
                Assert.Equal(3, spoken.Count); Assert.Equal("", words.Text); Assert.Equal("greeting", companion.WantedClip);
                clock.Advance(60); host.IdleTick(); Assert.Equal(3, spoken.Count);
            });
        }
        finally { await UiDispatcher.Run(() => { host?.Dispose(); owner?.Close(); }); }
    }

    // Headless activation does not deactivate the previous window automatically.
    private static void Deactivate(Window window) =>
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private static async Task<double> Advances(CompanionWindow companion, double time)
    {
        var before = 0; await UiDispatcher.Run(() => before = companion.DecodedFrames);
        await Until(() => companion.DecodedFrames > before, () => companion.Update(null, time += .1));
        return time;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }

    private static Point HitPoint(CompanionWindow companion)
    {
        var hit = companion.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "CompanionHit");
        return hit.TranslatePoint(new Point(hit.Bounds.Width / 2, hit.Bounds.Height / 2), companion)!.Value;
    }
    private static IEnumerable<Image> Portraits(CompanionWindow companion) =>
        companion.GetVisualDescendants().OfType<Image>().Where(image => image.Name != "CompanionTube");

    private static async Task Until(Func<bool> ready, Action update)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var complete = false; await UiDispatcher.Run(() => { update(); complete = ready(); });
            if (complete) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Companion frame was not decoded.");
    }
}
