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
    public async Task NativeCompanionFreezesForMotionGatesAndHidesWhenItsOwnerMinimizes()
    {
        Window owner = null!; CompanionWindow companion = null!; ClassicSettings settings = new() { CompanionEnabled = true };
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, settings, _ => { }, requireTransparency: false); companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0, () => companion.Update(null, .1));
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, .2); Assert.True(companion.IsVisible); Assert.Same(owner, companion.Owner);
                Assert.True(companion.IsMotionActive); settings.GentleVisuals = true;
            });
            var frozen = 0;
            await UiDispatcher.Run(() => { companion.Update(null, .3); frozen = companion.DecodedFrames; });
            await Task.Delay(50, TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() =>
            {
                companion.Update(null, 1); Assert.False(companion.IsMotionActive); Assert.Equal(frozen, companion.DecodedFrames);
                settings.GentleVisuals = false; settings.ReducedMotion = true;
                companion.Update(null, 2); Assert.False(companion.IsMotionActive);
                settings.ReducedMotion = false; settings.PerformanceMode = true;
                companion.Update(null, 3); Assert.False(companion.IsMotionActive);
                owner.WindowState = WindowState.Minimized; companion.Update(null, 4); Assert.False(companion.IsVisible);
                owner.WindowState = WindowState.Normal; settings.PerformanceMode = false; owner.Activate();
                companion.Update(null, 5); Assert.True(companion.IsVisible);
                typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);
                companion.Update(null, 6); Assert.False(companion.IsMotionActive);
            });
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
                companion.MouseDown(new Point(120, 200), MouseButton.Left); companion.MouseUp(new Point(120, 200), MouseButton.Left);
                companion.MouseDown(new Point(120, 200), MouseButton.Right); companion.MouseUp(new Point(120, 200), MouseButton.Right);
                Assert.Equal(1, clicked); Assert.Equal(1, stops); Assert.True(companion.IsVisible);
                companion.Close(); Dispatcher.UIThread.RunJobs(); Assert.Equal(2, stops); Assert.True(companion.IsVisible);
                companion.FinishImmediately(); Assert.False(companion.IsVisible);
                Assert.All(companion.GetVisualDescendants().OfType<Image>(), image => Assert.Null(image.Source));
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

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
