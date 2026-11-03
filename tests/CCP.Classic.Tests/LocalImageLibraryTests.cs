using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LocalImageLibraryTests
{
    [Theory]
    [InlineData(2048, 512, 1024, 256)]
    [InlineData(512, 2048, 256, 1024)]
    public async Task DecodesLazilyAtBoundedResolutionAndRetainsTheRealAspect(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var fixture = new ImageFixture();
        Bitmap fallback = null!; LocalImageLibrary library = null!;
        await UiDispatcher.Run(() =>
        {
            fallback = NewBitmap(8, 8);
            var path = fixture.Image("image.png", width, height);
            library = new([path, path, "https://example.invalid/image.png"], fallback);
            Assert.Single(library); Assert.Equal(0, library.CachedCount);
            Assert.Equal(fallback.Size, library[0].Size);
        });
        try
        {
            await library.Completion.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() =>
            {
                Assert.Equal(1, library.CachedCount);
                Assert.Equal(new Size(expectedWidth, expectedHeight), library[0].Size);
                Draw(library[0]);
                library.Dispose(); Assert.Equal(default, library[0].Size);
                using var usable = ((WriteableBitmap)fallback).Lock();
                Assert.NotEqual(IntPtr.Zero, usable.Address);
            });
        }
        finally { library.Dispose(); fallback.Dispose(); }
    }

    [Fact]
    public async Task WorkerQueueAndLruStayBoundedWithoutParallelDecodes()
    {
        using var fixture = new ImageFixture(); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken; var calls = 0; var active = 0; var peak = 0;
        Bitmap fallback = null!; LocalImageLibrary library = null!;
        await UiDispatcher.Run(() =>
        {
            fallback = NewBitmap(8, 8);
            var paths = Enumerable.Range(0, 30).Select(i => fixture.Image(i + ".png", 16, 8)).ToArray();
            library = new(paths, fallback, path =>
            {
                var concurrent = Interlocked.Increment(ref active); peak = Math.Max(peak, concurrent);
                Interlocked.Increment(ref calls); entered.TrySetResult();
                try { release.Wait(token); return new Bitmap(path); }
                finally { Interlocked.Decrement(ref active); }
            }, cacheCapacity: 2, pendingCapacity: 3);
            foreach (var image in library) _ = image.Size;
        });
        try
        {
            await entered.Task.WaitAsync(token);
            Assert.InRange(library.PendingCount, 1, 3); Assert.Equal(1, Volatile.Read(ref calls));
            release.Set(); await library.Completion.WaitAsync(token);
            Assert.Equal(1, peak); Assert.Equal(3, calls); Assert.Equal(2, library.CachedCount);
            await UiDispatcher.Run(() => Assert.Equal(new Size(16, 8), library[0].Size));
            await library.Completion.WaitAsync(token);
            Assert.Equal(4, calls); Assert.Equal(2, library.CachedCount);
        }
        finally { release.Set(); library.Dispose(); await library.Completion.WaitAsync(token); fallback.Dispose(); }
    }

    [Fact]
    public async Task DisposalHidesAndReleasesLateWorkWithoutDisposingTheBorrowedFallback()
    {
        using var fixture = new ImageFixture(); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        Bitmap fallback = null!; LocalImageLibrary library = null!;
        await UiDispatcher.Run(() =>
        {
            fallback = NewBitmap(8, 8); var path = fixture.Image("image.png", 16, 8);
            library = new([path], fallback, value => { entered.TrySetResult(); release.Wait(token); return new Bitmap(value); });
            _ = library[0].Size;
        });
        try
        {
            await entered.Task.WaitAsync(token); library.Dispose();
            await UiDispatcher.Run(() => { Assert.Equal(default, library[0].Size); Draw(library[0]); });
            release.Set(); await library.Completion.WaitAsync(token);
            Assert.Equal(0, library.CachedCount); Assert.Equal(0, library.PendingCount);
            await UiDispatcher.Run(() => { using var usable = ((WriteableBitmap)fallback).Lock(); Assert.NotEqual(IntPtr.Zero, usable.Address); });
        }
        finally { release.Set(); library.Dispose(); await library.Completion.WaitAsync(token); fallback.Dispose(); }
    }

    [Fact]
    public async Task MissingAndCorruptImagesStayOnFallbackWithoutAnEndlessRetryLoop()
    {
        using var fixture = new ImageFixture();
        Bitmap fallback = null!; LocalImageLibrary library = null!;
        await UiDispatcher.Run(() =>
        {
            fallback = NewBitmap(8, 8); var corrupt = Path.Combine(fixture.Root, "corrupt.png"); File.WriteAllText(corrupt, "bad");
            library = new([corrupt, Path.Combine(fixture.Root, "missing.png")], fallback);
            foreach (var image in library) _ = image.Size;
        });
        try
        {
            await library.Completion.WaitAsync(TestContext.Current.CancellationToken);
            var completed = library.Completion;
            await UiDispatcher.Run(() =>
            {
                Assert.All(library, image => { Assert.Equal(fallback.Size, image.Size); Draw(image); });
                Assert.Same(completed, library.Completion); Assert.Equal(0, library.CachedCount);
            });
        }
        finally { library.Dispose(); fallback.Dispose(); }
    }

    [Fact]
    public void NetworkAndDevicePathsAreRejectedBeforeFilesystemAccess()
    {
        Assert.Null(LocalImageLibrary.LocalPath("https://example.invalid/a.png"));
        Assert.Null(LocalImageLibrary.LocalPath("\\\\server\\share\\a.png"));
        Assert.Null(LocalImageLibrary.LocalPath("\\\\?\\C:\\a.png"));
        Assert.Null(LocalImageLibrary.LocalPath("relative.png"));
    }

    [Fact]
    public void UnixLinkedFilesAndLinkedAncestorsAreRejected()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ImageFixture();
        var target = Path.Combine(fixture.Root, "real.png"); File.WriteAllText(target, "image");
        var fileLink = Path.Combine(fixture.Root, "linked.png");
        var directoryLink = Path.Combine(fixture.Root, "linked-directory");
        File.CreateSymbolicLink(fileLink, target); Directory.CreateSymbolicLink(directoryLink, fixture.Root);
        try
        {
            Assert.Null(LocalImageLibrary.LocalPath(fileLink));
            Assert.Null(LocalImageLibrary.LocalPath(Path.Combine(directoryLink, "real.png")));
        }
        finally { File.Delete(fileLink); Directory.Delete(directoryLink); }
    }

    private static WriteableBitmap NewBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    private static void Draw(IImage image)
    {
        using var target = new RenderTargetBitmap(new PixelSize(32, 32));
        using var context = target.CreateDrawingContext();
        context.DrawImage(image, new Rect(image.Size), new Rect(0, 0, 32, 32));
    }
    private sealed class ImageFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public ImageFixture() => Directory.CreateDirectory(Root);
        public string Image(string name, int width, int height)
        {
            var path = Path.Combine(Root, name); using var bitmap = NewBitmap(width, height); bitmap.Save(path, PngBitmapEncoderOptions.Default); return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
