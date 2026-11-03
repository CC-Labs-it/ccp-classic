using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Media;

internal sealed partial class VideoSessionService(Window owner, ClassicSettings settings, SessionController session,
    Action<Window> configureInput, Func<IReadOnlyList<string>, CancellationToken, Task<MediaLibrarySnapshot>>? scanner = null,
    Func<string, double, bool, ILocalVideoPlayback>? playerFactory = null) : IDisposable
{
    private sealed class Run(VideoCue cue, string path)
    {
        public readonly VideoCue Cue = cue;
        public readonly string Path = path;
        public readonly List<VideoWindow> Windows = [];
        public int Attempts;
    }
    private readonly VideoTimeline timeline = new(settings);
    private readonly Dictionary<long, Run> runs = [];
    private readonly List<VideoWindow> soundRecency = [];
    private readonly Dictionary<string, double> lengths = new(StringComparer.Ordinal);
    private readonly Random random = new();
    private CancellationTokenSource? scanCancellation;
    private Task<MediaLibrarySnapshot>? scan;
    private string[]? files;
    private string[] scanRoots = [], eligibleFiles = [];
    private int selectionHash;
    private bool selectionDirty = true;
    private int nextFile;
    private bool running;
    public string? Status { get; private set; }
    public bool IsPlaying => runs.Count > 0 || previewWindow is not null;
    public bool IsBlocking => runs.Values.Any(run => !run.Cue.Floating) || previewBlocking;
    public Func<bool>? LaunchBlocked { get; set; }
    public bool CanTriggerBubble => running && LaunchBlocked?.Invoke() != true && eligibleFiles.Length > 0 && timeline.CanTriggerBubble;
    public bool TryTriggerBubble() => CanTriggerBubble && timeline.TryTriggerBubble();
    public bool TryAcceptToyAttention(double observedElapsed) => running && runs.Values.Where(run => run.Cue.Attention && !run.Cue.Floating)
        .SelectMany(run => run.Windows).Any(window => window.TryAcceptToyAttention(observedElapsed));
    public event Action<double>? VideoFinished;
    public event Action<bool>? CountAnswered;
    public event Action? AttentionStarted;
    public IReadOnlyList<AudioPlaybackSample> GetAudioSamples() => runs.Values.SelectMany(run => run.Windows)
        .Select(window => window.GetAudioSample()).OfType<AudioPlaybackSample>().ToArray();
    public IReadOnlyList<AudioPlaybackSample> GetTimelineSamples() => runs.Values.SelectMany(run => run.Windows)
        .Concat(previewWindow is { } preview ? [preview] : []).Select(window => window.GetTimelineSample())
        .OfType<AudioPlaybackSample>().DistinctBy(sample => (sample.Path, sample.SourceSeconds)).ToArray();

    public void Start()
    {
        Stop(); timeline.Start(); running = true; Status = null;
        RefreshLibrary(force: true);
    }
    public void RefreshLibrary(bool force = false)
    {
        if (!running) return;
        var roots = settings.Library.VideoFolders.ToArray();
        if (force || !scanRoots.SequenceEqual(roots))
        {
            scanCancellation?.Cancel(); scanCancellation?.Dispose(); scanCancellation = null;
            scanRoots = roots; files = null; eligibleFiles = []; selectionDirty = true;
            scanCancellation = new(); var token = scanCancellation.Token;
            scan = scanner?.Invoke(roots, token) ?? Task.Run(() => MediaLibrary.Scan(videoDirectories: roots, cancellationToken: token), token);
        }
        var hash = new HashCode(); hash.Add(settings.Video.MinDurationSeconds);
        foreach (var path in settings.Library.ExcludedFiles) hash.Add(path);
        foreach (var path in settings.Library.IncludedFiles) hash.Add(path);
        foreach (var entry in settings.Library.FolderSelections) { hash.Add(entry.Key); hash.Add(entry.Value); }
        var current = hash.ToHashCode();
        if (!selectionDirty && selectionHash == current) return;
        selectionHash = current; selectionDirty = false; Refilter();
    }
    private void Refilter() => eligibleFiles = files?.Where(path => settings.Library.Includes(path)
        && settings.Video.Eligible(lengths.GetValueOrDefault(path))).ToArray() ?? [];
    public void Tick(double elapsed)
    {
        if (!running) return;
        RefreshLibrary();
        if (files is null)
        {
            if (scan?.IsFaulted == true) { _ = scan.Exception; Status = "VideoUnavailable"; }
            if (scan?.IsCompletedSuccessfully == true) { files = scan.Result.Videos.ToArray(); Refilter(); }
            else if (runs.Count == 0) return;
        }
        var snapshot = timeline.Step(elapsed, files is null || LaunchBlocked?.Invoke() == true);
        var keep = snapshot.Active.Select(cue => cue.Id).ToHashSet();
        var gain = settings.Effects["audio"].Enabled
            ? settings.Effects["audio"].Values["master"] * settings.Effects["audio"].Values["video"] / 10000 : 0;
        foreach (var (id, run) in runs.ToArray())
        {
            if (!keep.Contains(id)) { Close(run); runs.Remove(id); continue; }
            foreach (var window in run.Windows.ToArray())
            {
                var hasSound = previewWindow is null && ReferenceEquals(window, soundRecency.LastOrDefault());
                window.Pulse(elapsed, hasSound ? gain : 0);
                if (!running) return;
                if (window.DurationSeconds > 0 && lengths.GetValueOrDefault(run.Path) != window.DurationSeconds)
                { lengths[run.Path] = window.DurationSeconds; selectionDirty = true; }
            }
            if (run.Windows.Count == 0 || !run.Windows.All(window => window.HasFinished)) continue;
            var first = run.Windows[0];
            var failed = first.HadAttention && !first.AttentionPassed;
            var surprise = first.HadAttention && first.AttentionPassed && random.NextDouble() < .1;
            var retry = first.PlaybackError is null && (failed || surprise)
                && (run.Cue.Strict || settings.Video.ReplayAfterFailedCheck);
            Close(run);
            if (retry && (!settings.Video.MercyEnabled || ++run.Attempts < settings.Video.MercyAfterFails)) Open(run);
            else { runs.Remove(id); timeline.Complete(id); }
        }
        foreach (var cue in snapshot.Launches)
        {
            if (!running) return;
            Refilter(); var eligible = eligibleFiles;
            if (eligible.Length == 0) { timeline.Complete(cue.Id); Status = "NoVideos"; continue; }
            var run = new Run(cue, eligible[nextFile++ % eligible.Length]); runs.Add(cue.Id, run); Open(run);
        }
    }
    private IReadOnlyList<Screen> Screens()
    {
        var screens = owner.Screens.All;
        if (settings.Video.TargetMonitor >= 0 && settings.Video.TargetMonitor < screens.Count) return [screens[settings.Video.TargetMonitor]];
        if (settings.Video.TargetMonitor == -2 || settings.FillAllMonitors) return screens.Take(8).ToArray();
        var main = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        return main is null ? [] : [main];
    }
    private void Open(Run run)
    {
        var screens = Screens();
        var selected = run.Cue.Floating ? screens.Skip((int)((run.Cue.Id - 1) % Math.Max(1, screens.Count))).Take(1).ToArray() : screens.ToArray();
        // Unknown metadata is eligible on its first cycle, as in CCP's background metadata cache.
        var source = playerFactory?.Invoke(run.Path, run.Cue.CapSeconds, settings.Video.RandomStart && settings.Video.MaxDurationSeconds > 0)
            ?? new LocalVideoPlayback(run.Path, run.Cue.CapSeconds, settings.Video.RandomStart && settings.Video.MaxDurationSeconds > 0);
        var shared = new SharedVideoPlayback(source);
        var checks = new VideoAttentionTimeline((int)run.Cue.Id);
        var sound = new VideoSoundState();
        try
        {
            for (var index = 0; index < Math.Max(1, selected.Length); index++)
            {
                var window = new VideoWindow(run.Cue, settings, shared.CreateView(), new LocalizationService(settings.Language), session.Stop, configureInput, checks, sound)
                { TargetScreen = selected.ElementAtOrDefault(index) };
                if (index == 0)
                {
                    window.VideoFinished += () => VideoFinished?.Invoke(window.PlayedSeconds);
                    window.CountAnswered += correct => CountAnswered?.Invoke(correct);
                    window.AttentionStarted += () => AttentionStarted?.Invoke();
                }
                window.Touched += () => Touch(window);
                run.Windows.Add(window); JoinSound(window, run.Cue.Floating && settings.Video.FloatingKeepSound);
                window.Begin(owner, (int)(run.Cue.Id % 4));
            }
            Status = null;
        }
        catch
        { Close(run); source.Dispose(); runs.Remove(run.Cue.Id); timeline.Complete(run.Cue.Id); Status = "VideoUnavailable"; }
    }
    private void JoinSound(VideoWindow window, bool keep)
    {
        if (keep && soundRecency.Count > 0) soundRecency.Insert(soundRecency.Count - 1, window);
        else soundRecency.Add(window);
    }
    private void Touch(VideoWindow window)
    { if (soundRecency.Remove(window)) soundRecency.Add(window); }
    private void Close(Run run)
    {
        foreach (var window in run.Windows) { soundRecency.Remove(window); window.FinishImmediately(); }
        run.Windows.Clear();
    }
    public void Stop()
    {
        StopPreview();
        running = false; scanCancellation?.Cancel(); scanCancellation?.Dispose(); scanCancellation = null;
        scan = null; files = null; scanRoots = []; eligibleFiles = []; selectionDirty = true; nextFile = 0; timeline.Stop();
        foreach (var run in runs.Values.ToArray()) Close(run);
        runs.Clear(); soundRecency.Clear();
    }
    public void Dispose() { disposed = true; Stop(); }
}
