using System.Diagnostics;
using Avalonia.Threading;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Media;

internal sealed partial class VideoSessionService
{
    private CancellationTokenSource? previewCancellation;
    private DispatcherTimer? previewTimer;
    private VideoWindow? previewWindow;
    private bool previewBlocking, disposed;
    public bool Preview()
    {
        if (disposed || LaunchBlocked?.Invoke() == true) return false;
        if (running)
        {
            RefreshLibrary();
            if (eligibleFiles.Length == 0) { if (files is not null) Status = "NoVideos"; return false; }
            return timeline.TryPreview();
        }
        StopPreview(); previewCancellation = new(); var token = previewCancellation.Token;
        var roots = settings.Library.VideoFolders.ToArray();
        _ = Task.Run(() =>
        {
            try
            {
                var snapshot = MediaLibrary.Scan(videoDirectories: roots, cancellationToken: token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested || disposed || LaunchBlocked?.Invoke() == true) return;
                    var path = snapshot.Videos.FirstOrDefault(settings.Library.Includes);
                    if (path is null) { Status = "NoVideos"; return; }
                    OpenPreview(path);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) Status = "VideoUnavailable"; }); }
        }, token);
        return true;
    }
    private void OpenPreview(string path)
    {
        var strict = running && settings.Effects["video"].Values["strict"] != 0;
        var floating = !running || !strict && settings.Effects["video"].Values["floating"] != 0;
        var cap = settings.Video.PlaybackCap(settings.Effects["video"].Values["cap"] * 60);
        var cue = new VideoCue(long.MaxValue, 0, cap, floating, strict,
            running && !floating && settings.Effects["video"].Values["attention"] != 0);
        var playback = playerFactory?.Invoke(path, cap, settings.Video.RandomStart && settings.Video.MaxDurationSeconds > 0)
            ?? new LocalVideoPlayback(path, cap, settings.Video.RandomStart && settings.Video.MaxDurationSeconds > 0);
        try
        {
            var window = new VideoWindow(cue, settings, playback, new LocalizationService(settings.Language), session.Stop, configureInput)
            { TargetScreen = Screens().FirstOrDefault() };
            previewWindow = window; previewBlocking = !floating;
            var clock = Stopwatch.StartNew();
            previewTimer = new(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) =>
            {
                if (!ReferenceEquals(previewWindow, window)) return;
                var gain = settings.Effects["audio"].Enabled
                    ? settings.Effects["audio"].Values["master"] * settings.Effects["audio"].Values["video"] / 10000 : 0;
                window.Pulse(clock.Elapsed.TotalSeconds, gain);
                if (window.HasFinished) StopPreview();
            });
            window.Begin(owner, 0); previewTimer.Start();
        }
        catch { StopPreview(); playback.Dispose(); Status = "VideoUnavailable"; }
    }
    private void StopPreview()
    {
        previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null;
        previewTimer?.Stop(); previewTimer = null;
        var window = previewWindow; previewWindow = null; previewBlocking = false; window?.FinishImmediately();
    }
}
