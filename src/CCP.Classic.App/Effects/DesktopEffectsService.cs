using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Classic.App.Input;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

// One shared frame clock, one transparent host per selected monitor, one audio worker.
public sealed class DesktopEffectsService : IDisposable
{
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly SessionController session;
    private readonly StarterPack starter = new();
    private readonly List<Bitmap> images = [];
    private readonly List<EffectOverlayWindow> windows = [];
    private readonly LibVlcAudioPlayback audio = new();
    private readonly GlobalPanicMonitor panic;
    private readonly EffectsTimeline timeline;
    private readonly DispatcherTimer timer;
    private readonly Action<Window> configureInput;
    private readonly TimeProvider clock = TimeProvider.System;
    private EffectsSnapshot? snapshot;
    private string? lastStatus;
    private long started;
    private bool running, lastAudioEnabled, disposed;
    public bool GlobalPanicAvailable => panic.IsAvailable;
    public event Action<string>? StatusChanged;

    public DesktopEffectsService(Window owner, ClassicSettings settings, SessionController session,
        Action<Window> configureInput, Action<string, bool> keyChanged)
    {
        this.owner = owner; this.settings = settings; this.session = session; this.configureInput = configureInput;
        foreach (var path in starter.Images)
        {
            try { images.Add(new Bitmap(path)); }
            catch (Exception e) when (e is IOException or ArgumentException) { }
        }
        timeline = new(settings, images.Count, starter.Phrases.Count);
        panic = new(() => settings.PanicKey, keyChanged);
        timer = new(TimeSpan.FromSeconds(1d / 30), DispatcherPriority.Background, (_, _) => Tick());
        session.Changed += SessionChanged;
        session.StopRequested += Stop;
    }

    public static bool IsPlayable(string id) => id is "flash" or "sub" or "spiral" or "pink" or "audio";

    private void SessionChanged()
    {
        if (session.IsRunning && !running) Start();
        else if (!session.IsRunning) Stop();
    }

    private void Start()
    {
        if (disposed) return;
        running = true; lastStatus = null;
        timeline.Start(); started = clock.GetTimestamp();
        lastAudioEnabled = settings.Effects["audio"].Enabled;
        timer.Start();
        Tick();
    }

    private void Tick()
    {
        if (!running || disposed) return;
        try
        {
            snapshot = timeline.Step(clock.GetElapsedTime(started).TotalSeconds);
            var anyVisual = settings.Effects["flash"].Enabled || settings.Effects["sub"].Enabled
                || settings.Effects["spiral"].Enabled || settings.Effects["pink"].Enabled;
            timer.Interval = TimeSpan.FromSeconds(anyVisual ? 1d / (settings.PerformanceMode ? 15 : 30) : .25);
            if (!anyVisual && snapshot.Flashes.Count == 0 && snapshot.TextOpacity + snapshot.SpiralOpacity + snapshot.PinkOpacity == 0)
                CloseWindows();
            if (anyVisual && windows.Count == 0 && !CreateWindows())
            {
                session.Dispose(); Report("EffectsUnavailable"); return;
            }
            var phrases = starter.Phrases;
            var spoken = snapshot.Phrase >= 0 ? phrases[snapshot.Phrase] : null;
            var frame = EffectsFrameBuilder.Build(snapshot, settings, images, phrases);
            var input = settings.Effects["flash"].Values["dismiss"] != 0
                ? frame.Flashes.Where(f => f.Opacity > .01).Select(f => f.Bounds).ToArray() : [];
            foreach (var window in windows)
            {
                if (!window.SetInputBounds(input)) { session.Dispose(); Report("EffectsUnavailable"); return; }
                window.View.UpdateFrame(frame);
            }
            var audioEnabled = settings.Effects["audio"].Enabled;
            if (lastAudioEnabled && !audioEnabled) audio.StopAll();
            lastAudioEnabled = audioEnabled;
            if (snapshot.Whisper && spoken?.AudioPath is { } clip)
                audio.Play(clip, settings.Effects["audio"].Values["master"] / 100);
            Report(!panic.IsAvailable ? "GlobalPanicUnavailable"
                : audio.LastError is not null ? "AudioUnavailable" : "Running");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            session.Dispose(); Report("EffectsUnavailable");
        }
    }

    private bool CreateWindows()
    {
        var selected = settings.FillAllMonitors ? owner.Screens.All
            : owner.Screens.All.Where(s => s.Bounds.Contains(owner.Position)).Take(1).ToArray();
        if (selected.Count == 0 && owner.Screens.Primary is { } primary) selected = [primary];
        foreach (var screen in selected)
        {
            var window = new EffectOverlayWindow(configureInput);
            windows.Add(window);
            window.FlashPressed += DismissAt;
            if (!window.Prepare(screen)) { CloseWindows(); return false; }
        }
        return windows.Count > 0;
    }

    private void DismissAt(Point point)
    {
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

    private void CloseWindows()
    {
        foreach (var window in windows) { window.View.UpdateFrame(EffectFrame.Empty); window.Close(); }
        windows.Clear();
    }

    public void Stop()
    {
        timer.Stop(); running = false; timeline.Stop(); snapshot = null;
        CloseWindows(); audio.StopAll();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Stop();
        session.Changed -= SessionChanged; session.StopRequested -= Stop;
        panic.Dispose(); audio.Dispose();
        foreach (var image in images) image.Dispose();
        images.Clear();
    }
}
