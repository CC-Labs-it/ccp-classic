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
    // CCP's public avatar0 clips at 5ad49a0c3fea777e32a065badee8a40bad2ed4a6 (docs/ASSET-PROVENANCE.md).
    private static readonly Dictionary<string, string> ClipHashes = new()
    {
        ["adoring"] = "4c0f158a12a13bb122003530d4a6121af34bccc5db65c29552e151e6d52f0848",
        ["blowkiss"] = "b61a6beca0df2c55f760ea729f2af2fd44b9103f796a6ca8812a57af9ea8d7d1",
        ["celebrate"] = "343ec1a4ad07fe479825857e8b8660d35460244dac2832f9861fb44ccdfa4447",
        ["cheer"] = "dddd46669be5d7181135e07134b5584b16442262a83b2972196264ff9f490b8b",
        ["comehere"] = "e35490f3ff51cd4c2b62a25862fa1a30caa7d03cf65b90a41a83bceeb0c47914",
        ["denial"] = "ca7a02b0724b51006189fafa8c49459f22aab0bc6772bd8d161f2f18abfce4f7",
        ["entrancing"] = "b3f2fc12a998755f938666ff535a9e60e51426099bd42c6e3baeff9b29cb6995",
        ["flirt"] = "05b2de8e8d34294c855a18aac980e257d68e90b124d0141cac76b6515fc47a2f",
        ["giggle"] = "228682c0af3d2a0b57c78254db14369dbaf42191d7d0b6f3cad9038fb2708379",
        ["greeting"] = "84cc43cbf520a38623d8d9bfcea452a1be6f269dd78ec022ae9d95f7cc9eaa3a",
        ["idle"] = "9a8a1707902c1392d827c8b987a5150deb194818c33dd52fe50fbb0bff6602aa",
        ["idle3"] = "ce42a54556c0a6d3447e63a42ccb4c1a2ccf287f6e9fecb268d1e8622e63f5b5",
        ["idle4"] = "0eadd0ced42b225055f1ecf564523eb9abfb32dcc448c122d95ae0edfa035218",
        ["idle5"] = "d354d54cdf7a3d027026f401f154cf6b67e88c028ed5f78dfc4cc9cfa0c7e6b3",
        ["praise"] = "7b23fdd17894c89ff674c1901fa1afff0a87e4a19b2738c794846e272ba21c7f",
        ["shy"] = "9e09b0e87f0382734d1322e0f5fd0626758649ef0298072f11dc7a0cc0b731bf",
        ["sultry"] = "d0edf61a4145a3b84e1ffa45956c8d99df599ef53f4f0606c938a1d3bf28a6f0",
        ["surprised"] = "c2e61b75173180585a8060aeb65fea561b494e526d4ab3154572db52d5deaeac",
        ["talkA"] = "5588d0ff0a05a5fee9934a0fd196a760f47ec4aec8c8f8299e4d639cc7c85859",
        ["talkB"] = "0b0299cc0d466767a2e93a2336e5e95700381c3cf1f49d757067ccffd6b3e6b9",
        ["talkC"] = "06ca41a5eb19aaa873aeca1791c4c8fa7e80165f4b2c6d0a951d14824c864a9e",
        ["talkD"] = "a5fc14ffd0d0c60a35ef63ac5d6bcef60c58137fbab8d2963883e811c2ad8902",
        ["talkE"] = "361c043ca0c383e62476aca8ca73c444b1be4a9736c1473ad9f99ba6d2a1351e",
        ["talkF"] = "f14f74fcfdb96a78adc2adf0cb85690cbaeb81b3b12ac259aa6489a2b768fe72",
        ["tease"] = "9374812f1c49827dc758a0d281bb4eb1aaea1c47ce2aa9da56ce69976be19141",
        ["tender"] = "d2dceee9d9a280d3d96fde1da1809adcb50c6eb7f9fbed34be55fda1a82fa2cb",
        ["wink"] = "7dccbb06920e4fb7ab03fd85d9fe754a49b0b7ed73d5a10d9ec327f78e42bea0"
    };

    // Every clip the shipped rules can name exists, matches its public hash and decodes as one 7.2 s pass of
    // 120 transparent 60 ms frames. The app reads the same rules the source tree holds.
    [Fact]
    public Task EveryClipTheShippedRulesNameIsAnOriginalTubeClip() => UiDispatcher.Run(() =>
    {
        var map = CompanionWindow.EmoteMap;
        Assert.Equal(CompanionEmoteDirectorTests.LoadMap().Clips.Order(), map.Clips.Order());
        Assert.Equal(ClipHashes.Keys.Order(), map.Clips.Order());
        foreach (var name in map.Clips)
        {
            var uri = new Uri($"avares://CCP.Classic.App/Assets/Companion/{name}.gif");
            Assert.True(AssetLoader.Exists(uri), name);
            using (var original = AssetLoader.Open(uri))
                Assert.Equal(ClipHashes[name], Convert.ToHexString(SHA256.HashData(original)), StringComparer.OrdinalIgnoreCase);
            using var source = AssetLoader.Open(uri); using var data = SKData.Create(source);
            using var codec = SKCodec.Create(data); Assert.NotNull(codec);
            Assert.Equal(120, codec.FrameCount); Assert.Equal(192, codec.Info.Width); Assert.Equal(309, codec.Info.Height);
            Assert.All(codec.FrameInfo, frame => Assert.Equal(60, frame.Duration));
            Assert.Equal(map.NativeSeconds(name), codec.FrameInfo.Sum(frame => frame.Duration) / 1000d, 9);
            using var bitmap = new SKBitmap(new SKImageInfo(192, 309, SKColorType.Bgra8888, SKAlphaType.Premul));
            foreach (var frame in new[] { 0, 60, 119 })
            {
                Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(frame)));
                Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            }
        }
        Assert.False(AssetLoader.Exists(new Uri("avares://CCP.Classic.App/Assets/Companion/idle2.gif")));
    });

    // CCP's fadeMs crossfade: the old clip stays fully visible until the new one has a frame, then both
    // dissolve over one second.
    [Fact]
    public async Task ANewClipCrossfadesOverTheOldOneOnceItsFirstFrameIsReady()
    {
        Window owner = null!; CompanionWindow companion = null!;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, new() { CompanionEnabled = true }, _ => { }, requireTransparency: false,
                    emotes: new(CompanionWindow.EmoteMap, new Random(9)));
                companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0 && companion.IsVisible, () => companion.Update(null, 0));
            await UiDispatcher.Run(() =>
            {
                Assert.Contains(companion.WantedClip, CompanionWindow.EmoteMap.Idle.Select(item => item.Clip));
                Assert.Single(Portraits(companion), image => image.Source is not null && image.Opacity == 1);
                companion.NonVerbal(5);
            });
            await Until(() => companion.IsCrossfading, () => companion.Update(null, 5));
            await UiDispatcher.Run(() =>
            {
                Assert.Contains(companion.WantedClip, CompanionWindow.EmoteMap.Expressive);
                var layers = Portraits(companion).ToList();
                Assert.All(layers, image => Assert.NotNull(image.Source));
                Assert.Equal(new[] { 0d, 1d }, layers.Select(image => image.Opacity).Order());
                companion.Update(null, 5.5);
                Assert.All(layers, image => Assert.Equal(.5, image.Opacity, 6));
                companion.Update(null, 6.01);
                Assert.False(companion.IsCrossfading);
                Assert.Equal(new[] { 0d, 1d }, layers.Select(image => image.Opacity).Order());
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

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
                // A session word is a trigger word: one expressive clip, never the talking clips.
                Assert.DoesNotContain(companion.WantedClip, CompanionWindow.EmoteMap.TalkPool);
                Assert.NotEqual(CompanionEmoteKind.Talk, companion.Emotes.Kind);
                // Closing the tube still asks the owner to stop; the stop itself is the owner's panic path.
                // Right-click opens the menu, whose Stop also goes to the owner (CompanionMenuTests).
                var point = HitPoint(companion);
                companion.MouseDown(point, MouseButton.Right); companion.MouseUp(point, MouseButton.Right);
                Assert.Equal(0, stops); Assert.True(companion.Menu.IsOpen); companion.Menu.Close();
                companion.Close(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, stops);
                host.EndSession();
                Assert.Equal(CompanionEmoteKind.Idle, companion.Emotes.Kind); Assert.Empty(companion.Emotes.Pending);
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
                // The word is a single trigger word: one expressive clip from idle, no talking.
                var expressive = companion.WantedClip;
                Assert.Contains(expressive, CompanionWindow.EmoteMap.Expressive);
                Assert.Equal(CompanionEmoteKind.Reaction, companion.Emotes.Kind);
                // A left click answers at once; a second click inside the cooldown does not stack another word.
                clock.Advance(1); var point = HitPoint(companion);
                companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, clicks); Assert.Equal(new[] { "Breathe", "Relax" }, spoken.Select(bark => bark.Text));
                // The click emote follows once the expressive clip has had its two seconds; the clicked word,
                // arriving while she reacts, does not replace either one.
                Assert.Equal("Relax", words.Text); Assert.Equal(expressive, companion.WantedClip);
                var click = Assert.Single(companion.Emotes.Pending);
                Assert.Contains(click.Clip, CompanionWindow.EmoteMap.ClickEmotes); Assert.Equal(host.Now + 1, click.At, 6);
                clock.Advance(.5); companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left);
                Assert.Equal(2, clicks); Assert.Equal(2, spoken.Count);
                // A stop silences the word at once; the next one waits a full interval instead of firing straight away.
                host.EndSession(); Assert.Equal("", words.Text);
                Assert.Empty(companion.Emotes.Pending); Assert.Contains(companion.WantedClip, CompanionWindow.EmoteMap.Idle.Select(item => item.Clip));
                clock.Advance(5.9); host.IdleTick(); Assert.Equal(2, spoken.Count);
                clock.Advance(.1); host.IdleTick(); Assert.Equal(3, spoken.Count);
                // A minimised panel says nothing and does not save words up for its return.
                owner.WindowState = WindowState.Minimized; clock.Advance(60); host.IdleTick(); Assert.Equal(3, spoken.Count);
                owner.WindowState = WindowState.Normal; owner.Activate(); host.IdleTick(); Assert.Equal(3, spoken.Count);
                // Barks off: a click still plays a click emote, with no word.
                settings.BarksEnabled = false; clock.Advance(2); host.IdleTick(); clock.Advance(1); host.IdleTick();
                companion.MouseDown(point, MouseButton.Left); companion.MouseUp(point, MouseButton.Left); host.IdleTick();
                Assert.Equal(3, spoken.Count); Assert.Equal("", words.Text);
                Assert.Contains(companion.WantedClip, CompanionWindow.EmoteMap.ClickEmotes);
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
