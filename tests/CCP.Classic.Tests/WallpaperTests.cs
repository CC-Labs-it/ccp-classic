using CCP.Classic.App.Wallpaper;
using CCP.Classic.App.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class WallpaperTests
{
    [Fact]
    public async Task JournalExistsBeforeTheDesktopChangesAndStopRestoresTheOriginalFile()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native();
        await using var wallpaper = fixture.Service(native);
        Assert.Equal(0, native.Captures); Assert.Empty(native.Writes);
        native.BeforeApply = () => Assert.True(File.Exists(Path.Combine(fixture.Data, "restore.json")));
        Assert.True(await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken));
        Assert.Equal(fixture.Selected, native.Current); Assert.Equal(WallpaperStatus.Active, wallpaper.Status);
        Assert.True(await wallpaper.StopAsync()); Assert.Equal(fixture.Original, native.Current);
        Assert.False(File.Exists(Path.Combine(fixture.Data, "restore.json")));
        Assert.Empty(Directory.GetFiles(fixture.Data, "original-*"));
    }

    [Fact]
    public async Task UnsafeDesktopConfigurationsAndUnverifiedImagesStayUntouched()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native(); native.CanReplace = false;
        await using var wallpaper = fixture.Service(native);
        Assert.False(await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken));
        Assert.Empty(native.Writes); Assert.Equal(fixture.Original, native.Current);
        await using var invalid = new StaticWallpaperService(native, fixture.Data, _ => false);
        Assert.False(await invalid.StartAsync(fixture.Selected, TestContext.Current.CancellationToken)); Assert.Empty(native.Writes);
    }

    [Fact]
    public async Task StopInvalidatesAStartBlockedInsideTheNativeWrite()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native();
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.BeforeApply = () => { entered.TrySetResult(); release.Wait(TestContext.Current.CancellationToken); };
        await using var wallpaper = fixture.Service(native);
        var start = wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stop = wallpaper.StopAsync(); release.Set();
        Assert.False(await start); Assert.True(await stop); Assert.Equal(fixture.Original, native.Current);
        Assert.Equal(fixture.Original, native.Writes[^1]);
    }

    [Fact]
    public async Task FailedRestoreKeepsTheVerifiedBreadcrumbForTheNextLaunch()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native();
        var wallpaper = fixture.Service(native);
        await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken);
        native.RestoreWorks = false;
        Assert.False(await wallpaper.StopAsync()); Assert.Equal(WallpaperStatus.RestoreFailed, wallpaper.Status);
        await wallpaper.DisposeAsync();
        Assert.True(File.Exists(Path.Combine(fixture.Data, "restore.json")));
        native.RestoreWorks = true;
        await using var next = fixture.Service(native);
        Assert.True(await next.RecoverAsync()); Assert.Equal(fixture.Original, native.Current);
        Assert.Equal(WallpaperStatus.Recovered, next.Status);
    }

    [Fact]
    public async Task ADeletedOriginalUsesTheFrozenImageWithoutDeletingWindowsRestoreTarget()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native();
        await using var wallpaper = fixture.Service(native);
        await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken); File.Delete(fixture.Original);
        Assert.True(await wallpaper.StopAsync()); Assert.True(File.Exists(native.Current));
        Assert.StartsWith(fixture.Data, native.Current); Assert.EndsWith(".png", native.Current);
    }

    [Fact]
    public async Task ASecondInstanceAndATamperedJournalCannotChangeTheDesktop()
    {
        using var fixture = new WallpaperFixture(); var firstNative = fixture.Native();
        await using var first = fixture.Service(firstNative);
        await first.StartAsync(fixture.Selected, TestContext.Current.CancellationToken);
        var secondNative = fixture.Native(); await using var second = fixture.Service(secondNative);
        Assert.False(await second.StartAsync(fixture.Selected, TestContext.Current.CancellationToken)); Assert.Empty(secondNative.Writes);
        await first.StopAsync();
        File.WriteAllText(Path.Combine(fixture.Data, "restore.json"), "{\"Version\":1}");
        Assert.False(await second.RecoverAsync()); Assert.Empty(secondNative.Writes);
    }

    [Fact]
    public async Task SolidColorRestoresByClearingTheImageAndNeverCapturesAStaleFile()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native(); native.SolidColor = true;
        await using var wallpaper = fixture.Service(native);
        Assert.True(await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(fixture.Data, "original-*"));
        Assert.True(await wallpaper.StopAsync()); Assert.Equal("", native.Current);
    }

    [Fact]
    public async Task AChangedFrozenBackupIsNotTrustedAndItsJournalIsRetained()
    {
        using var fixture = new WallpaperFixture(); var native = fixture.Native();
        await using var wallpaper = fixture.Service(native);
        await wallpaper.StartAsync(fixture.Selected, TestContext.Current.CancellationToken);
        File.WriteAllText(Assert.Single(Directory.GetFiles(fixture.Data, "original-*")), "tampered");
        Assert.False(await wallpaper.StopAsync()); Assert.Single(native.Writes);
        Assert.True(File.Exists(Path.Combine(fixture.Data, "restore.json")));
        Assert.Equal(WallpaperStatus.RestoreFailed, wallpaper.Status);
    }

    private sealed class FakeNative(string original) : IWallpaperNative
    {
        public bool IsSupported => true;
        public string Current = original;
        public readonly List<string> Writes = [];
        public int Captures; public bool CanReplace = true, RestoreWorks = true, SolidColor;
        public Action? BeforeApply;
        public WallpaperSnapshot Capture() { Captures++; return new(SolidColor ? "" : Current, SolidColor, "10", "0", CanReplace); }
        public bool Apply(string path) { BeforeApply?.Invoke(); Writes.Add(path); Current = path; return true; }
        public bool Restore(WallpaperSnapshot snapshot, string path)
        { if (!RestoreWorks) return false; Writes.Add(path); Current = path; return true; }
    }
    private sealed class WallpaperFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "wallpaper");
        public string Original => Path.Combine(Root, "original.png");
        public string Selected => Path.Combine(Root, "selected.png");
        public WallpaperFixture() { Directory.CreateDirectory(Root); File.WriteAllText(Original, "original"); File.WriteAllText(Selected, "selected"); }
        public FakeNative Native() => new(Original);
        public StaticWallpaperService Service(FakeNative native) => new(native, Data, _ => true);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

