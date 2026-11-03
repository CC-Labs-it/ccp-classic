using Avalonia.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Media;

internal sealed class VideoSessionService(Window owner, ClassicSettings settings, SessionController session,
    Action<Window> configureInput) : IDisposable
{
    private readonly VideoTimeline timeline = new(settings);
    private readonly Dictionary<long, VideoWindow> windows = [];
    private CancellationTokenSource? scanCancellation;
    private Task<MediaLibrarySnapshot>? scan;
    private string[]? files;
    private int nextFile;
    private bool running;
    public string? Status { get; private set; }
    public bool IsPlaying => windows.Count > 0;
    public event Action<double>? VideoFinished;
    public event Action<bool>? CountAnswered;

    public void Start()
    {
        Stop(); timeline.Start(); running = true; Status = null;
        scanCancellation = new(); var token = scanCancellation.Token;
        var roots = settings.Library.VideoFolders.ToArray();
        scan = Task.Run(() => MediaLibrary.Scan(videoDirectories: roots, cancellationToken: token), token);
    }

    public void Tick(double elapsed)
    {
        if (!running) return;
        if (files is null)
        {
            if (scan?.IsFaulted == true) { _ = scan.Exception; Status = "VideoUnavailable"; return; }
            if (scan?.IsCompletedSuccessfully != true) return;
            files = scan.Result.Videos.Where(settings.Library.Includes).ToArray();
        }
        var snapshot = timeline.Step(elapsed);
        var keep = snapshot.Active.Select(cue => cue.Id).ToHashSet();
        foreach (var (id, window) in windows.ToArray())
        {
            if (!keep.Contains(id)) { window.FinishImmediately(); windows.Remove(id); continue; }
            var gain = settings.Effects["audio"].Enabled
                ? settings.Effects["audio"].Values["master"] * settings.Effects["audio"].Values["video"] / 10000 : 0;
            window.Pulse(elapsed, gain);
            if (!running) return;
            if (window.HasFinished)
            {
                if (window.PlaybackError is not null) Status = "VideoUnavailable";
                windows.Remove(id); timeline.Complete(id);
            }
        }
        foreach (var cue in snapshot.Launches)
        {
            if (!running) return;
            if (files.Length == 0) { timeline.Complete(cue.Id); Status = "NoVideos"; continue; }
            var playback = new LocalVideoPlayback(files[nextFile++ % files.Length], cue.CapSeconds);
            var window = new VideoWindow(cue, settings, playback, new LocalizationService(settings.Language), session.Stop, configureInput);
            window.VideoFinished += () => VideoFinished?.Invoke(window.PlayedSeconds);
            window.CountAnswered += correct => CountAnswered?.Invoke(correct);
            try { windows.Add(cue.Id, window); window.Begin(owner, windows.Count - 1); Status = null; }
            catch { window.FinishImmediately(); windows.Remove(cue.Id); timeline.Complete(cue.Id); Status = "VideoUnavailable"; }
        }
    }

    public void Stop()
    {
        running = false;
        scanCancellation?.Cancel(); scanCancellation?.Dispose(); scanCancellation = null;
        scan = null; files = null; nextFile = 0; timeline.Stop();
        foreach (var window in windows.Values.ToArray()) window.FinishImmediately();
        windows.Clear();
    }
    public void Dispose() => Stop();
}
