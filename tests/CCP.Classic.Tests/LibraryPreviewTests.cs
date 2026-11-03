using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LibraryPreviewTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 3)]
    [InlineData(961, 1, 4)]
    public Task MalformedVideoFramesFailOnlyThePreview(int width, int height, int bytes) => UiDispatcher.Run(() =>
    {
        using var run = new Run(); run.Playback.Frame = new(new byte[bytes], width, height, 1);
        run.Tick(); Assert.Equal(1, run.Playback.Disposals); Assert.Null(run.Picture.Source);
        Assert.Contains(run.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == run.Text["PreviewUnavailable"]);
        Assert.True(run.Window.IsVisible);
    });

    [Fact]
    public Task AVideoWithoutFramesStopsAtThePreviewWatchdog() => UiDispatcher.Run(() =>
    {
        using var run = new Run(); run.Clock.Advance(14); run.Tick(); Assert.Equal(0, run.Playback.Disposals);
        run.Clock.Advance(1); run.Tick(); Assert.Equal(1, run.Playback.Disposals);
        Assert.Contains(run.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == run.Text["PreviewUnavailable"]);
    });

    [Fact]
    public Task ReShowingDoesNotReopenPlaybackAndCloseClearsPixelsBeforeDisposal() => UiDispatcher.Run(() =>
    {
        using var run = new Run(); run.Playback.Frame = new([1, 2, 3, 255], 1, 1, 1); run.Tick(); var picture = run.Picture; Assert.NotNull(picture.Source);
        run.Window.Hide(); run.Window.Show(run.Owner); Assert.Equal(1, run.Opens);
        run.Playback.Disposing = () => { Assert.Null(picture.Source); throw new InvalidOperationException("Fake teardown failure"); };
        run.Window.Close(); Assert.False(run.Window.IsVisible); Assert.Equal(1, run.Playback.Disposals); Assert.Null(picture.Source);
    });

    private sealed class Run : IDisposable
    {
        public readonly Window Owner = new();
        public readonly Playback Playback = new();
        public readonly Clock Clock = new();
        public readonly LocalizationService Text = new("en");
        public readonly LibraryPreviewWindow Window;
        public int Opens;
        public Image Picture => Window.GetVisualDescendants().OfType<Image>().Single();
        public Run()
        {
            Owner.Show();
            Window = new(new(Path.Combine(Path.GetTempPath(), "preview-local.mp4"), "Videos"), new ClassicSettings(), Text, _ => { },
                _ => { Opens++; return Playback; }, Clock);
            Window.Show(Owner);
        }
        public void Tick() => typeof(LibraryPreviewWindow).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, null);
        public void Dispose() { Window.Close(); Owner.Close(); }
    }
    private sealed class Playback : ILocalVideoPlayback
    {
        public VideoFrame? Frame;
        public int Disposals;
        public Action? Disposing;
        public bool IsFinished => false;
        public string? LastError => null;
        public void Start() { }
        public void SetGain(double gain) { }
        public void SetPaused(bool paused) { }
        public bool TryCopyFrame(out VideoFrame? frame) { frame = Frame; Frame = null; return frame is not null; }
        public void Dispose() { Disposals++; Disposing?.Invoke(); }
    }
    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(int seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
