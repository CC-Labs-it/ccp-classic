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
    private readonly RemixSession remix;
    private readonly DispatcherTimer timer;
    private readonly Action<Window> configureInput;
    private readonly TimeProvider clock = TimeProvider.System;
    private EffectsSnapshot? snapshot;
    private string? lastStatus;
    private long started;
    private bool running, disposed;
    private long lastFlash;
    private InAppEffectWindow? fallback;
    private bool useFallback;
    private readonly LiveLoom loom;
    public bool GlobalPanicAvailable => panic.IsAvailable;
    public event Action<string>? StatusChanged;
    public event Action<double>? VideoFinished;
    public event Action<bool>? CountAnswered;
    public event Action? FlashShown;

    public DesktopEffectsService(Window owner, ClassicSettings settings, SessionController session,
        Action<Window> configureInput, Action<string, bool> keyChanged)
    {
        this.owner = owner; this.settings = settings; this.session = session; this.configureInput = configureInput;
        AudioOutputSelection.Configure(settings.Audio);
        loom = new(settings, new LoomStore(LoomStore.DefaultDirectory));
        whispers = new(audio);
        videos = new(owner, settings, session, configureInput);
        videos.VideoFinished += seconds => VideoFinished?.Invoke(seconds);
        videos.CountAnswered += correct =>
        {
            if (correct) popSounds.Play(cueGate, settings.Effects["audio"].Enabled, settings.Bubbles.VolumePercent / 100d);
            CountAnswered?.Invoke(correct);
        };
        InitializeInteractive();
        videos.LaunchBlocked = () => lockFrame.LockCard is not null || interactiveFrame.CascadeActive;
        InitializeBrainDrain();
        InitializeSpiralMedia();
        InitializeHaptics();
        foreach (var path in starter.Images)
        {
            try { images.Add(new Bitmap(path)); }
            catch (Exception e) when (e is IOException or ArgumentException) { }
        }
        timeline = new(settings, images.Count, starter.Phrases.Count);
        remix = new(settings, () => selectedImages?.SelectedPaths ?? []);
        timeline.ScheduledRemix = remix.TryTake;
        timeline.FlashAudioRequested += BeginFlashVoice;
        timeline.ImageAspectRatio = index =>
        {
            var image = selectedImages is { } library ? library[index] : images[index];
            return image.Size.Width / Math.Max(1, image.Size.Height);
        };
        panic = new(() => settings.PanicKey, keyChanged, () => settings.KeywordTriggersEnabled && session.IsRunning);
        InitializeCompanion();
        InitializeWallpaper();
        timer = new(TimeSpan.FromSeconds(1d / 30), DispatcherPriority.Background, (_, _) => Tick());
        session.Changed += SessionChanged;
        session.StopRequested += Stop;
    }

    public static bool IsPlayable(string id) => id is "flash" or "sub" or "spiral" or "pink" or "audio" or "video"
        or "bubble" or "bounce" or "lock" or "wipe" or "drain" || id == "wall" && OperatingSystem.IsWindows();

    public void RefreshSettings() { AudioOutputSelection.Configure(settings.Audio); RefreshInteractiveLibrary(); Tick(); }
    public void ReloadLibrary() { RefreshInteractiveLibrary(force: true); videos.RefreshLibrary(force: true); Tick(); }
    public void ReloadCustomSpiral() { loom.Reload(); Tick(); }

    private void SessionChanged()
    {
        if (session.IsRunning && !running) Start();
        else if (!session.IsRunning) Stop();
    }

    private void Start()
    {
        if (disposed) return;
        running = true; useFallback = false; lastStatus = null;
        timeline.Start(); videos.Start(); StartInteractive(); StartCompanion(); lastFlash = 0; started = clock.GetTimestamp();
        StartBrainDrain(); StartHaptics();
        StartSpiralMedia();
        remix.Start();
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
            TickBrainDrain(elapsed);
            TickFlashVoice(elapsed);
            TickSpiralMedia(elapsed);
            selectedImages?.AdvanceAnimations(elapsed, !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode, settings.Flash.GifSpeed);
            TickCompanion(elapsed);
            TickWallpaper();
            timeline.SetPhrases(settings.Subliminal.Phrases);
            remix.BeforeFrame(elapsed, !videos.IsPlaying && lockFrame.LockCard is null && !interactiveFrame.CascadeActive);
            snapshot = timeline.Step(elapsed);
            remix.AfterFrame(snapshot.Flashes.Any(flash => flash.IsRemix));
            HapticFlashLuminance(snapshot.Flashes.Where(flash => flash.Opacity > .01 && flash.Image >= 0).Select(flash => flash.Image));
            loom.Update(elapsed);
            foreach (var flash in snapshot.Flashes.Where(flash => flash.Id > lastFlash).OrderBy(flash => flash.Id))
            { lastFlash = flash.Id; FlashShown?.Invoke(); }
            var anyVisual = settings.Effects["flash"].Enabled || settings.Effects["sub"].Enabled
                || settings.Effects["spiral"].Enabled || settings.Effects["pink"].Enabled
                || settings.Effects["bubble"].Enabled || settings.Effects["bounce"].Enabled || settings.Effects["drain"].Enabled;
            if (!anyVisual && snapshot.Flashes.Count == 0 && snapshot.TextOpacity + snapshot.SpiralOpacity + snapshot.PinkOpacity == 0
                && interactiveFrame.Bubbles.Count == 0 && interactiveFrame.BouncingText is null && interactiveFrame.ImageVisuals.Count == 0)
                CloseWindows();
            var needsHost = anyVisual || snapshot.Flashes.Count > 0 || snapshot.TextOpacity + snapshot.SpiralOpacity + snapshot.PinkOpacity > 0
                || interactiveFrame.Bubbles.Count > 0 || interactiveFrame.BouncingText is not null || interactiveFrame.ImageVisuals.Count > 0;
            timer.Interval = FrameInterval(settings, videos.IsPlaying, lockFrame.LockCard is not null, needsHost || companionWindow?.IsVisible == true);
            if (needsHost && !useFallback && !ReconcileWindows())
            {
                windows.Clear(CloseWindow); useFallback = true;
            }
            var phrases = starter.Phrases;
            var spoken = ResolveSubliminal(snapshot);
            IReadOnlyList<IImage> frameImages = selectedImages is { } library ? library : images;
            var frame = EffectsFrameBuilder.Build(snapshot, settings, frameImages, phrases, remix.Image)
                with { Interactive = interactiveFrame, CustomSpiral = loom.Image };
            if (useFallback && needsHost)
            {
                if (fallback is null)
                {
                    fallback = new(owner, new(settings.Language), configureInput, session.Stop);
                    fallback.InputPressed += DismissAt;
                    fallback.TryBeginDrag = TryBeginFlashDrag;
                    fallback.DragMoved += point => timeline.MoveDrag(point.X, point.Y);
                    fallback.DragFinished += timeline.EndDrag;
                }
                fallback.UpdateFrame(ProjectBouncingText(ApplySpiralMedia(FrameForWindow(frame, fallback), null), fallback));
            }
            foreach (var window in windows.Hosts)
            {
                var windowFrame = ProjectBouncingText(ApplySpiralMedia(FrameForWindow(frame, window), owner.Screens.ScreenFromWindow(window)), window);
                var monitorFlashInput = settings.Effects["flash"].Values["dismiss"] != 0
                    ? windowFrame.Flashes.Where(f => f.Exit is null && f.Shatter is null && f.Opacity > .01).Select(f => f.InputBounds) : [];
                var input = monitorFlashInput.Concat(windowFrame.Interactive.Bubbles.Where(b => b.CanPop)
                    .Select(b => BubbleGeometry.NormalizedBounds(b, window.ClientSize.Width, window.ClientSize.Height))).ToArray();
                if (!window.SetInputBounds(input)) { windows.Clear(CloseWindow); useFallback = true; break; }
                window.View.UpdateFrame(windowFrame);
            }
            whispers.Update(settings, snapshot.Whisper, spoken?.AudioPath,
                settings.Audio.DuckingEnabled && (videos.IsPlaying || KeywordDucking || FlashDucking) ? 1 - settings.Effects["audio"].Values["duck"] / 100 : 1, snapshot.ManualSubliminal);
            TickHaptics(elapsed);
            Report(videos.Status ?? BrainDrainStatus ?? SpiralStatus ?? wallpaper?.StatusKey ?? HapticStatus ?? (useFallback ? "EffectsInWindow" : null)
                ?? (companionWindow?.LastError is not null ? "CompanionUnavailable" : null) ?? (!panic.IsAvailable ? "GlobalPanicUnavailable"
                : audio.LastError is not null ? "AudioUnavailable" : "Running"));
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            session.Abort(); Report("EffectsUnavailable");
        }
    }

    internal static TimeSpan FrameInterval(ClassicSettings settings, bool videoPlaying, bool lockShowing, bool transientSurface = false)
    {
        var hasSurface = transientSurface || videoPlaying || lockShowing || settings.CompanionEnabled
            || new[] { "flash", "sub", "spiral", "pink", "bubble", "bounce", "drain" }.Any(id => settings.Effects[id].Enabled);
        return TimeSpan.FromSeconds(hasSurface ? 1d / (settings.PerformanceMode ? 15 : 30) : .25);
    }

    private bool ReconcileWindows()
    {
        IReadOnlyList<Screen> selected = settings.FillAllMonitors ? owner.Screens.All
            : owner.Screens.ScreenFromWindow(owner) is { } current ? [current] : [];
        if (selected.Count == 0 && owner.Screens.Primary is { } primary) selected = [primary];
        if (settings.Effects["spiral"].Enabled || snapshot is { SpiralOpacity: > 0 })
            selected = selected.Concat(SpiralScreens()).Distinct().ToArray();
        var monitor = settings.PinkFilter.Monitor;
        if ((settings.Effects["pink"].Enabled || snapshot is { PinkOpacity: > 0 }) && monitor >= 0 && monitor < owner.Screens.All.Count)
            selected = selected.Append(owner.Screens.All[monitor]).Distinct().ToArray();
        var targets = selected.GroupBy(screen => new MonitorTarget(screen.Bounds, screen.Scaling))
            .ToDictionary(group => group.Key, group => group.First());
        return windows.Reconcile(targets.Keys, target =>
        {
            var window = new EffectOverlayWindow(configureInput);
            window.FlashPressed += point => DismissAt(point, window.ClientSize);
            window.TryBeginDrag = point => TryBeginFlashDrag(point, window.ClientSize);
            window.DragMoved += point => timeline.MoveDrag(point.X, point.Y);
            window.DragFinished += timeline.EndDrag;
            var prepared = false;
            try
            {
                prepared = window.Prepare(targets[target]);
                return prepared ? window : null;
            }
            finally { if (!prepared) CloseWindow(window); }
        }, CloseWindow);
    }

    private void DismissAt(Point point, Size viewport)
    {
        var bubble = interactiveFrame.Bubbles.LastOrDefault(b => b.CanPop
            && BubbleGeometry.HitTest(b, point, viewport.Width, viewport.Height));
        if (bubble is not null && interactive.Pop(bubble.Id)) return;
        if (snapshot is null) return;
        var flash = snapshot.Flashes.LastOrDefault(f => f.Exit is null && f.Shatter is null && f.Opacity > .01
            && new Rect(f.HitArea.X, f.HitArea.Y, f.HitArea.Width, f.HitArea.Height).Contains(point));
        if (flash is not null) timeline.Dismiss(flash.Id);
    }

    private bool TryBeginFlashDrag(Point point, Size viewport)
    {
        if (interactiveFrame.Bubbles.Any(b => b.CanPop && BubbleGeometry.HitTest(b, point, viewport.Width, viewport.Height))) return false;
        var flash = snapshot?.Flashes.LastOrDefault(f => f.Exit is null && f.Shatter is null && f.Opacity > .01
            && new Rect(f.HitArea.X, f.HitArea.Y, f.HitArea.Width, f.HitArea.Height).Contains(point));
        return flash is not null && timeline.BeginDrag(flash.Id, point.X, point.Y);
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

    private void CloseWindows()
    {
        windows.Clear(CloseWindow); fallback?.FinishImmediately(); fallback = null;
    }

    public void Stop()
    {
        timer.Stop(); running = false; StopHaptics(); timeline.Stop(); snapshot = null;
        CloseWindows(); whispers.Stop(); videos.Stop(); StopFlashVoice(); StopBrainDrain(); StopSpiralMedia(); StopInteractive(); StopCompanion();
        remix.Stop();
        _ = StopWallpaperAsync();
        loom.Reset();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Stop();
        session.Changed -= SessionChanged; session.StopRequested -= Stop;
        panic.Dispose(); audio.Dispose(); DisposeInteractiveAudio(); DisposeBrainDrain(); DisposeSpiralMedia(); DisposeWallpaper();
        loom.Dispose(); flashAudio.Dispose(); remix.Dispose();
        DisposeHaptics();
        foreach (var image in images) image.Dispose();
        images.Clear();
    }
}
