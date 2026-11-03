using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class MediaLibraryTests
{
    [Fact]
    public void RecursiveLibrariesAreFilteredSortedAndDeduplicated()
    {
        using var fixture = new LibraryFixture();
        var nested = Directory.CreateDirectory(Path.Combine(fixture.Root, "nested")).FullName;
        var image = fixture.File("z.PNG");
        var nestedImage = fixture.File("nested/a.jpg");
        var video = fixture.File("clip.MKV");
        var audio = fixture.File("sound.flac");
        fixture.File("ignored.txt"); fixture.File("list.m3u"); fixture.File("link.url");

        var snapshot = MediaLibrary.Scan([fixture.Root, nested, fixture.Root], [fixture.Root], [fixture.Root],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { nestedImage, image }.Order(StringComparer.Ordinal), snapshot.Images);
        Assert.Equal([video], snapshot.Videos);
        Assert.Equal([audio], snapshot.Audio);
        Assert.False(snapshot.IsTruncated);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Images).Clear());
        fixture.File("new.png");
        Assert.Equal(2, snapshot.Images.Count);
    }

    [Theory]
    [InlineData("https://example.invalid/image.png")]
    [InlineData("file:///C:/media")]
    [InlineData("\\\\server\\share\\media")]
    [InlineData("//server/share/media")]
    [InlineData("\\\\?\\C:\\media")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    [InlineData("relative/media")]
    [InlineData("C:media")]
    [InlineData("")]
    public void RemoteDeviceAndRelativePathsAreRejected(string path)
    {
        Assert.False(MediaLibrary.IsLocalPath(path));
        Assert.Empty(MediaLibrary.Scan([path], cancellationToken: TestContext.Current.CancellationToken).Images);
    }

    [Fact]
    public void MissingAndNonDirectoryRootsDoNotHideOtherLibraries()
    {
        using var fixture = new LibraryFixture();
        var image = fixture.File("image.png");
        var missing = Path.Combine(fixture.Root, "missing");
        var snapshot = MediaLibrary.Scan([missing, image, fixture.Root], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([image], snapshot.Images);
        Assert.Empty(snapshot.Videos); Assert.Empty(snapshot.Audio);
        Assert.True(MediaLibrary.IsLocalPath(Path.Combine(fixture.Root, ".", "missing")));
        Assert.False(snapshot.IsTruncated);
    }

    [Fact]
    public void TotalFileBudgetBoundsAllKinds()
    {
        using var fixture = new LibraryFixture();
        fixture.File("one.png"); fixture.File("two.png"); fixture.File("three.mp4");
        var snapshot = MediaLibrary.Scan([fixture.Root], [fixture.Root], maximumFiles: 2,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, snapshot.Images.Count);
        Assert.Empty(snapshot.Videos);
        Assert.True(snapshot.IsTruncated);
    }

    [Fact]
    public void UnsupportedFilesAlsoConsumeABoundedTraversalBudget()
    {
        using var fixture = new LibraryFixture();
        for (var index = 0; index < 101; index++) fixture.File($"{index}.txt");
        var snapshot = MediaLibrary.Scan([fixture.Root], maximumFiles: 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(snapshot.Images);
        Assert.True(snapshot.IsTruncated);
    }

    [Fact]
    public void CancelledScanDoesNotReturnAPartialSuccessfulSnapshot()
    {
        using var fixture = new LibraryFixture();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => MediaLibrary.Scan([fixture.Root], cancellationToken: stop.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaLibrary.Scan(maximumFiles: 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaLibrary.Scan(maximumFiles: 100001, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CancellationWhileEnumeratingRootsIsObserved()
    {
        using var fixture = new LibraryFixture();
        using var stop = new CancellationTokenSource();
        IEnumerable<string> Roots()
        {
            yield return fixture.Root;
            stop.Cancel();
            yield return fixture.Root;
        }
        Assert.Throws<OperationCanceledException>(() => MediaLibrary.Scan(Roots(), cancellationToken: stop.Token));
    }

    [Fact]
    public void UnixSymbolicLinksCannotEscapeTheSelectedLibraryOrBecomeARoot()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new LibraryFixture();
        using var outside = new LibraryFixture();
        fixture.File("inside.png"); outside.File("outside.png");
        var directoryLink = Path.Combine(fixture.Root, "linked");
        var fileLink = Path.Combine(fixture.Root, "linked.png");
        Directory.CreateSymbolicLink(directoryLink, outside.Root);
        System.IO.File.CreateSymbolicLink(fileLink, Path.Combine(outside.Root, "outside.png"));
        try
        {
            Assert.Single(MediaLibrary.Scan([fixture.Root], cancellationToken: TestContext.Current.CancellationToken).Images);
            Assert.Empty(MediaLibrary.Scan([directoryLink], cancellationToken: TestContext.Current.CancellationToken).Images);
            var descendant = Directory.CreateDirectory(Path.Combine(outside.Root, "child")).FullName;
            System.IO.File.WriteAllText(Path.Combine(descendant, "child.png"), "test");
            Assert.Empty(MediaLibrary.Scan([Path.Combine(directoryLink, "child")], cancellationToken: TestContext.Current.CancellationToken).Images);
        }
        finally
        {
            Directory.Delete(directoryLink); System.IO.File.Delete(fileLink);
        }
    }

    private sealed class LibraryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public LibraryFixture() => Directory.CreateDirectory(Root);
        public string File(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
            System.IO.File.WriteAllText(path, "test"); return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
