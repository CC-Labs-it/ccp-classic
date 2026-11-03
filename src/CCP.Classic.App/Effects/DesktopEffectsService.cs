using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.App.Input;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

// One shared frame clock, one transparent host per monitor, independently cancellable audio lanes.
public sealed partial class DesktopEffectsService : IDisposable
{
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly SessionController session;
    private readonly StarterPack starter = new();
    private readonly List<Bitmap> images = [];
    private readonly OverlayHostSet<MonitorTarget, EffectOverlayWindow> windows = new();
    private readonly LibVlcAudioPlayback audio = new();
    private readonly RecordedWhisperPlayback whispers;
    private readonly VideoSessionService videos;
    private readonly GlobalPanicMonitor panic;
    private readonly EffectsTimeline timeline;
    private readonly DispatcherTimer timer;
    private readonly Action<Window> configureInput;
    private readonly TimeProvider clock = TimeProvider.System;
    private EffectsSnapshot? snapshot;
    private string? lastStatus;
    private long started;
    private bool running, disposed;
    private long lastFlash;
    public bool GlobalPanicAvailable => panic.IsAvailable;
    public event Action<string>? StatusChanged;
    public event Action<double>? VideoFinished;
    public event Action<bool>? CountAnswered;
    public event Action? FlashShown;

    public DesktopEffectsService(Window owner, ClassicSettings settings, SessionController session,
        Action<Window> configureInput, Action<string, bool> keyChanged)
    {
        this.owner = owner; this.settings = settings; this.session = session; this.configureInput = configureInput;
        whispers = new(audio);
        videos = new(owner, settings, session, configureInput);
        videos.VideoFinished += seconds => VideoFinished?.Invoke(seconds);
        videos.CountAnswered += correct => CountAnswered?.Invoke(correct);
        InitializeInteractive();
        foreach (var path in starter.Images)
        {
            try { images.Add(new Bitmap(path)); }
            catch (Exception e) when (e is IOException or ArgumentException) { }
        }
        timeline = new(settings, images.Count, starter.Phrases.Count);
        panic = new(() => settings.PanicKey, keyChanged, () => settings.KeywordTriggersEnabled && session.IsRunning);
        InitializeCompanion();
        timer = new(TimeSpan.FromSeconds(1d / 30), DispatcherPriority.Background, (_, _) => Tick());
        session.Changed += SessionChanged;
        session.StopRequested += Stop;
    }

    public static bool IsPlayable(string id) => id is "flash" or "sub" or "spiral" or "pink" or "audio" or "video"
        or "bubble" or "bounce" or "lock" or "wipe" or "drain";

    private void SessionChanged()
    {
        if (session.IsRunning && !running) Start();
        else if (!session.IsRunning) Stop();
    }

    private void Start()
    {
        if (disposed) return;
        running = true; lastStatus = null;
        timeline.Start(); videos.Start(); StartInteractive(); StartCompanion(); lastFlash = 0; started = clock.GetTimestamp();
        timer.Start();
        Tick();
    }

    private void Tick()
    {
        if (!running || disposed) return;
        try
        {
            var elapsed = clock.GetElapsedTime(started).TotalSeconds;
            videos.Tick(elapsed);
            TickInteractive(elapsed);
            TickCompanion(elapsed);
            snapshot = timeline.Step(elapsed);
            foreach (var flash in snapshot.Flashes.Where(flash => flash.Id > lastFlash).OrderBy(flash => flash.Id))
            { lastFlash = flash.Id; FlashShown?.Invoke(); }
            var anyVisual = settings.Effects["flash"].Enabled || settings.Effects["sub"].Enabled
                || settings.Effects["spiral"].Enabled || settings.Effects["pink"].Enabled
                || settings.Effects["bubble"].Enabled || settings.Effects["bounce"].Enabled || settings.Effects["drain"].Enabled;
            timer.Interval = TimeSpan.FromSeconds(anyVisual ? 1d / (settings.PerformanceMode ? 15 : 30) : .25);
            if (!anyVisual && snapshot.Flashes.Count == 0 && snapshot.TextOpacity + snapshot.SpiralOpacity + snapshot.PinkOpacity == 0
                && interactiveFrame.Bubbles.Count == 0 && interactiveFrame.BouncingText is null && lockFrame.BrainDrain is null)
                CloseWindows();
            var needsHost = anyVisual || snapshot.Flashes.Count > 0 || snapshot.TextOpacity + snapshot.SpiralOpacity + snapshot.PinkOpacity > 0
                || interactiveFrame.Bubbles.Count > 0 || interactiveFrame.BouncingText is not null || lockFrame.BrainDrain is not null;
            if (needsHost && !ReconcileWindows())
            {
                session.Dispose(); Report("EffectsUnavailable"); return;
            }
            var phrases = starter.Phrases;
            var spoken = snapshot.Phrase >= 0 ? phrases[snapshot.Phrase] : null;
            IReadOnlyList<IImage> frameImages = selectedImages is { } library ? library : images;
            var frame = EffectsFrameBuilder.Build(snapshot, settings, frameImages, phrases)
                with { Interactive = interactiveFrame, BrainDrain = lockFrame.BrainDrain };
            var flashInput = settings.Effects["flash"].Values["dismiss"] != 0
                ? frame.Flashes.Where(f => f.Opacity > .01).Select(f => f.Bounds) : [];
            var input = flashInput.Concat(interactiveFrame.Bubbles.Where(b => b.CanPop)
                .Select(b => new Rect(b.X, b.Y, b.Size, b.Size))).ToArray();
            foreach (var window in windows.Hosts)
            {
                if (!window.SetInputBounds(input)) { session.Dispose(); Report("EffectsUnavailable"); return; }
                window.View.UpdateFrame(frame);
            }
            whispers.Update(settings, snapshot.Whisper, spoken?.AudioPath,
                videos.IsPlaying ? 1 - settings.Effects["audio"].Values["duck"] / 100 : 1);
            Report(videos.Status ?? (companionWindow?.LastError is not null ? "CompanionUnavailable" : null) ?? (!panic.IsAvailable ? "GlobalPanicUnavailable"
                : audio.LastError is not null ? "AudioUnavailable" : "Running"));
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            session.Dispose(); Report("EffectsUnavailable");
        }
    }

    private bool ReconcileWindows()
    {
        IReadOnlyList<Screen> selected = settings.FillAllMonitors ? owner.Screens.All
            : owner.Screens.ScreenFromWindow(owner) is { } current ? [current] : [];
        if (selected.Count == 0 && owner.Screens.Primary is { } primary) selected = [primary];
        var targets = selected.GroupBy(screen => new MonitorTarget(screen.Bounds, screen.Scaling))
            .ToDictionary(group => group.Key, group => group.First());
        return windows.Reconcile(targets.Keys, target =>
        {
            var window = new EffectOverlayWindow(configureInput);
            window.FlashPressed += DismissAt;
            var prepared = false;
            try
            {
                prepared = window.Prepare(targets[target]);
                return prepared ? window : null;
            }
            finally { if (!prepared) CloseWindow(window); }
        }, CloseWindow);
    }

    private void DismissAt(Point point)
    {
        var bubble = interactiveFrame.Bubbles.LastOrDefault(b => b.CanPop && new Rect(b.X, b.Y, b.Size, b.Size).Contains(point));
        if (bubble is not null && interactive.Pop(bubble.Id)) return;
        if (snapshot is null) return;
        var flash = snapshot.Flashes.LastOrDefault(f => f.Opacity > .01
            && new Rect(f.X, f.Y, f.Size, f.Size).Contains(point));
        if (flash is not null) timeline.Dismiss(flash.Id);
    }

    private void Report(string key)
    {
        if (lastStatus == key) return;
        lastStatus = key; StatusChanged?.Invoke(key);
    }

    private static void CloseWindow(EffectOverlayWindow window)
    {
        window.View.UpdateFrame(EffectFrame.Empty);
        window.Close();
    }

    private void CloseWindows() => windows.Clear(CloseWindow);

    public void Stop()
    {
        timer.Stop(); running = false; timeline.Stop(); snapshot = null;
        CloseWindows(); whispers.Stop(); videos.Stop(); StopInteractive(); StopCompanion();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Stop();
        session.Changed -= SessionChanged; session.StopRequested -= Stop;
        panic.Dispose(); audio.Dispose(); DisposeInteractiveAudio();
        foreach (var image in images) image.Dispose();
        images.Clear();
    }
}
