using Avalonia;
using Avalonia.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class VideoLibraryRefreshTests
{
    [Fact]
    public Task SelectionEditsBlockFutureCuesWhileTheCurrentlyPlayingClipKeepsItsCapturedPath() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var scans = 0;
        using var service = new VideoSessionService(fixture.Owner, fixture.Settings, fixture.Session, _ => { },
            (_, _) => { scans++; return Task.FromResult(Snapshot(fixture.First, fixture.Second)); }, fixture.CreatePlayer);
        service.Start(); service.Tick(0); Assert.Single(fixture.Players); Assert.Equal(fixture.First, fixture.Paths[0]);
        fixture.Settings.Library.Include(fixture.Second, false); service.RefreshLibrary(); service.Tick(.25);
        Assert.Equal(0, fixture.Players[0].Disposals); Assert.Equal(1, scans);
        fixture.Settings.Library.Include(fixture.First, false); service.RefreshLibrary();
        Assert.False(service.CanTriggerBubble); Assert.False(service.Preview()); service.Tick(180);
        Assert.Single(fixture.Players); Assert.Equal(0, fixture.Players[0].Disposals);
        fixture.Settings.Library.Include(fixture.Second, true); service.RefreshLibrary(); Assert.True(service.Preview()); service.Tick(181);
        Assert.Equal(fixture.Second, fixture.Paths[^1]); Assert.Equal(1, fixture.Players[0].Disposals); Assert.Equal(1, scans);
    });

    [Fact]
    public Task RootRescanCancelsTheOldGenerationWithoutFreezingActivePlayersOrPublishingStaleFiles() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var scans = new List<(CancellationToken Token, TaskCompletionSource<MediaLibrarySnapshot> Result)>();
        using var service = new VideoSessionService(fixture.Owner, fixture.Settings, fixture.Session, _ => { },
            (_, token) => { var result = new TaskCompletionSource<MediaLibrarySnapshot>(); scans.Add((token, result)); return result.Task; }, fixture.CreatePlayer);
        service.Start(); scans[0].Result.SetResult(Snapshot(fixture.First)); service.Tick(0); service.Tick(.25);
        var original = Assert.Single(fixture.Players); var copies = original.Copies;
        fixture.Settings.Library.VideoFolders.Add(fixture.NewRoot); service.RefreshLibrary(); service.Tick(.5);
        Assert.True(scans[0].Token.IsCancellationRequested); Assert.True(original.Copies > copies); Assert.Equal(0, original.Disposals);
        service.RefreshLibrary(force: true); Assert.True(scans[1].Token.IsCancellationRequested);
        scans[1].Result.SetResult(Snapshot(fixture.First)); service.Tick(.75); Assert.Single(fixture.Players);
        scans[2].Result.SetResult(Snapshot(fixture.Second)); service.Tick(1); Assert.True(service.Preview()); service.Tick(1.25);
        Assert.Equal(fixture.Second, fixture.Paths[^1]); Assert.Equal(1, original.Disposals);
        service.Stop(); Assert.True(scans[2].Token.IsCancellationRequested); Assert.All(fixture.Players, p => Assert.Equal(1, p.Disposals));
    });

    private static MediaLibrarySnapshot Snapshot(params string[] files) => MediaLibrary.Scan(
        videoDirectories: files.Select(path => Path.GetDirectoryName(path)!).Distinct(), cancellationToken: TestContext.Current.CancellationToken);
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "CCP Classic video selection", Guid.NewGuid().ToString("N"));
        public string First => Path.Combine(root, "a", "a.mp4");
        public string Second => Path.Combine(root, "b", "b.mp4");
        public string NewRoot => Path.Combine(root, "new");
        public readonly Window Owner = new();
        public readonly ClassicSettings Settings = new();
        public readonly SessionController Session;
        public readonly List<Player> Players = [];
        public readonly List<string> Paths = [];
        public Fixture()
        {
            Directory.CreateDirectory(NewRoot); Directory.CreateDirectory(Path.GetDirectoryName(First)!); Directory.CreateDirectory(Path.GetDirectoryName(Second)!);
            File.WriteAllBytes(First, [1]); File.WriteAllBytes(Second, [2]);
            Settings.Library.VideoFolders = [root]; Settings.Effects["video"].Enabled = true;
            Settings.Effects["video"].Values["launch"] = 1; Settings.Effects["video"].Values["maximum"] = 1;
            Settings.Effects["video"].Values["rate"] = 20; Session = new(Settings); Owner.Show(); Session.Start();
        }
        public ILocalVideoPlayback CreatePlayer(string path, double cap, bool random)
        { var player = new Player(); Players.Add(player); Paths.Add(path); return player; }
        public void Dispose() { Session.Dispose(); Owner.Close(); Directory.Delete(root, true); }
    }
    private sealed class Player : ILocalVideoPlayback
    {
        public bool IsFinished => false;
        public string? LastError => null;
        public int Disposals, Copies;
        private long sequence;
        public void Start() { }
        public void SetGain(double gain) { }
        public void SetPaused(bool paused) { }
        public bool TryCopyFrame(out VideoFrame? frame) { Copies++; frame = new([1, 2, 3, 255], 1, 1, ++sequence); return true; }
        public void Dispose() => Disposals++;
    }
}
