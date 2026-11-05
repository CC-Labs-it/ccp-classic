using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Classic.App;
using CCP.Classic.App.Companion;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class OwnerClosingTests
{
    [Fact]
    public Task ClosingTheAppEndsPanicDisabledSessionsWithStrictVideoAndCompanionChildren() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings { CompanionEnabled = true };
        var owner = new MainWindow(settings, windows: true);
        var playback = new Playback(); var stopRequests = 0;
        var video = new VideoWindow(new(1, 0, 60, false, true, false), settings, playback,
            new LocalizationService(), owner.Session.Stop, _ => { });
        var companion = new CompanionWindow(owner, settings, _ => { }, requireTransparency: false);
        companion.StopRequested += () => stopRequests++;
        try
        {
            owner.Show(); owner.Session.Start(); owner.Session.SetPanicEnabled(false, true);
            video.Begin(owner, 0); video.Pulse(0, 1); video.Pulse(.25, 1);
            companion.Show(owner);
            var videoImage = ((Grid)video.Content!).Children.OfType<Image>().Single(image => image.Name == "VideoImage");
            Assert.True(video.IsVisible); Assert.True(companion.IsVisible); Assert.True(owner.Session.IsRunning);
            Assert.NotNull(videoImage.Source);

            owner.Close(); Dispatcher.UIThread.RunJobs();

            Assert.False(owner.IsVisible); Assert.False(owner.Session.IsRunning);
            Assert.False(video.IsVisible); Assert.True(video.HasFinished); Assert.Equal(1, playback.Disposals);
            Assert.Null(videoImage.Source);
            Assert.False(companion.IsVisible); Assert.Equal(0, stopRequests);
        }
        finally
        {
            video.FinishImmediately(); companion.FinishImmediately();
            if (owner.IsVisible) owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task ACancelingOwnedWindowCannotBlockMainWindowClosure() => UiDispatcher.Run(() =>
    {
        var owner = new MainWindow(windows: true); var child = new Window();
        var childClosed = false; var childClosingRequests = 0;
        child.Closing += (_, args) => { childClosingRequests++; args.Cancel = true; };
        child.Closed += (_, _) => childClosed = true;
        try
        {
            owner.Show(); owner.Session.Start(); owner.Session.SetPanicEnabled(false, true); child.Show(owner);
            owner.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(owner.IsVisible); Assert.False(owner.Session.IsRunning);
            Assert.True(childClosed); Assert.Equal(0, childClosingRequests);
        }
        finally
        {
            if (owner.IsVisible) { owner.ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly; owner.Close(); }
            Dispatcher.UIThread.RunJobs();
        }
    });

    private sealed class Playback : ILocalVideoPlayback
    {
        private bool copied;
        public int Disposals;
        public bool IsFinished => Disposals > 0;
        public string? LastError => null;
        public void Start() { }
        public void SetGain(double gain) { }
        public void SetPaused(bool paused) { }
        public bool TryCopyFrame(out VideoFrame? frame)
        {
            frame = copied ? null : new([1, 2, 3, 255], 1, 1, 1); copied = true; return frame is not null;
        }
        public void Dispose() => Disposals++;
    }
}
