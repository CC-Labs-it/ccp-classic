using Avalonia;
using Avalonia.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private readonly OverlayHostSet<MonitorTarget, BrainDrainWindow> drainWindows = new();
    private readonly LibVlcAudioPlayback drainAudio = new();
    private readonly Random drainRandom = new();
    private AudioPlaybackGate drainGate = null!;
    private BrainDrainCaptureWorker? drainCapture;
    private Task drainRetiring = Task.CompletedTask;
    private CancellationTokenSource? drainScanStop;
    private Task<MediaLibrarySnapshot>? drainScan;
    private string[] drainFolders = [];
    private AudioPlaybackTicket? drainTicket;
    private string? drainStatus;
    private double drainNextRoll, drainHoldUntil, drainFade, drainFadeFrom, drainFadeTarget, drainFadeAt, drainLastTime, drainMotionSeconds, drainRetryAt;
    private bool drainAllowCapture, drainAudioNotified, drainVisualNotified, drainScanFailed;
    private double drainInterval = 5;
    private double drainCaptureStarted;
    private double drainStrength = 50;
    private bool drainLastMelt;
    public string? BrainDrainStatus => drainStatus ?? (drainAudio.LastError is not null || drainScanFailed ? "AudioUnavailable" : null);
    internal bool BrainDrainIsVisible => drainFade > .001 && drainWindows.Hosts.Any();

    private void InitializeBrainDrain() => drainGate = new(drainAudio);
    private void StartBrainDrain()
    {
        StopBrainDrain(); drainNextRoll = drainInterval = settings.Effects["drain"].Values["refresh"] != 0 ? .5 : 5;
        drainLastTime = drainMotionSeconds = drainRetryAt = 0;
        ReloadBrainDrainAudio();
    }
    public void ReloadBrainDrainAudio()
    {
        drainScanStop?.Cancel(); drainScanStop?.Dispose(); drainScanStop = null; drainScan = null;
        drainFolders = settings.BrainDrain.AudioFolders.ToArray(); drainScanFailed = false;
        if (!running || disposed) return;
        drainScanStop = new(); var token = drainScanStop.Token; var roots = drainFolders;
        drainScan = Task.Run(() => MediaLibrary.Scan(audioDirectories: roots, cancellationToken: token), token);
    }
    public bool TriggerBrainDrainPulse()
    {
        if (!running || disposed || !session.IsRunning || settings.BrainDrain.BlurStrength <= 0
            || settings.Effects["drain"].Enabled || drainHoldUntil > drainLastTime) return false;
        drainHoldUntil = drainLastTime + 10; return true;
    }
    private void TickBrainDrain(double elapsed)
    {
        var dt = Math.Max(0, elapsed - drainLastTime); drainLastTime = elapsed;
        TickBrainDrainAudio(elapsed);
        var options = settings.BrainDrain;
        // BrainDrainVisualPolicy: zero is fully off, without touching the independent audio lane.
        if (options.BlurStrength <= 0) { StopBrainDrainCapture(); drainFade = drainFadeTarget = 0; drainStatus = null; return; }
        var desired = settings.Effects["drain"].Enabled || elapsed < drainHoldUntil;
        if (!settings.GentleVisuals && !settings.ReducedMotion) drainMotionSeconds += dt;
        if (desired)
        {
            drainStrength = options.BlurStrength * (settings.GentleVisuals ? .5 : 1);
            drainLastMelt = (options.MeltEnabled || elapsed < drainHoldUntil)
                && !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode;
        }
        var alpha = desired ? BrainDrainFrameProcessor.AlphaFor(drainStrength) : 0;
        if (alpha != drainFadeTarget)
        { drainFadeFrom = drainFade; drainFadeTarget = alpha; drainFadeAt = elapsed; }
        var p = Math.Clamp((elapsed - drainFadeAt) / (settings.GentleVisuals ? .75 : .3), 0, 1);
        drainFade = drainFadeFrom + (drainFadeTarget - drainFadeFrom) * p * p * (3 - 2 * p);
        if (!desired && drainFade <= .001)
        { StopBrainDrainCapture(); drainStatus = null; return; }
        if (!OperatingSystem.IsWindows())
        { StopBrainDrainCapture(); drainStatus = "BrainDrainUnavailable"; return; }
        if (elapsed < drainRetryAt) return;
        if (drainCapture is null)
        {
            if (!drainRetiring.IsCompleted) { drainStatus = "BrainDrainUnavailable"; return; }
            if (!ReconcileBrainDrainWindows()) { DrainCaptureFailed(elapsed); return; }
            drainCapture = new(); drainCaptureStarted = elapsed; drainVisualNotified = false;
        }
        else if (!ReconcileBrainDrainWindows()) { DrainCaptureFailed(elapsed); return; }
        if (drainAllowCapture != options.AllowCapture)
        {
            if (drainWindows.Hosts.Any(window => !window.SetCapturePolicy(options.AllowCapture))) { DrainCaptureFailed(elapsed); return; }
            drainAllowCapture = options.AllowCapture;
        }
        var screens = drainWindows.Hosts.Select(window => window.CaptureBounds).ToArray();
        drainCapture.Update(new(screens, new(drainStrength, drainLastMelt, drainMotionSeconds), settings.PerformanceMode,
            settings.PerformanceMode ? 15 : settings.GentleVisuals || settings.ReducedMotion ? 10 : 30));
        var fresh = drainCapture.TakeFrames();
        foreach (var window in drainWindows.Hosts)
        {
            if (fresh.Remove(window.CaptureBounds, out var frame)) window.Present(frame);
            window.Opacity = Math.Clamp(drainFade, 0, 1);
        }
        foreach (var frame in fresh.Values) frame.Dispose();
        drainStatus = drainCapture.Status ?? (drainCapture.FramesPublished == 0 && elapsed - drainCaptureStarted >= 3 ? "BrainDrainUnavailable" : null);
        if (!drainVisualNotified && drainCapture.FramesPublished > 0)
        { drainVisualNotified = true; DeviceEvent?.Invoke(LocalDeviceEvent.BrainDrain); }
        if (drainCapture.Completion.IsCompleted && drainCapture.Status is not null) DrainCaptureFailed(elapsed);
    }
    private bool ReconcileBrainDrainWindows()
    {
        IReadOnlyList<Avalonia.Platform.Screen> chosen = settings.FillAllMonitors ? owner.Screens.All
            : owner.Screens.ScreenFromWindow(owner) is { } current ? [current] : Array.Empty<Avalonia.Platform.Screen>();
        var targets = chosen.Take(8).GroupBy(screen => new MonitorTarget(screen.Bounds, screen.Scaling))
            .ToDictionary(group => group.Key, group => group.First());
        return drainWindows.Reconcile(targets.Keys, target =>
        {
            var child = new BrainDrainWindow(configureInput); var ready = false;
            try { ready = child.Prepare(targets[target], settings.BrainDrain.AllowCapture); return ready ? child : null; }
            finally { if (!ready) child.Finish(); }
        }, window => window.Finish());
    }
    private void DrainCaptureFailed(double elapsed)
    { StopBrainDrainCapture(); drainStatus = "BrainDrainUnavailable"; drainRetryAt = elapsed + 2; }
    private void StopBrainDrainCapture()
    {
        if (drainCapture is { } old) { old.Dispose(); drainRetiring = old.Completion; drainCapture = null; }
        drainWindows.Clear(window => window.Finish()); drainVisualNotified = false;
    }
    private void TickBrainDrainAudio(double elapsed)
    {
        if (!drainFolders.SequenceEqual(settings.BrainDrain.AudioFolders)) ReloadBrainDrainAudio();
        if (drainScan?.IsFaulted == true) { _ = drainScan.Exception; drainScanFailed = true; }
        var interval = settings.Effects["drain"].Values["refresh"] != 0 ? .5 : 5;
        if (interval != drainInterval) { drainInterval = interval; drainNextRoll = elapsed + interval; }
        var enabled = settings.Effects["drain"].Enabled && settings.Effects["audio"].Enabled && settings.Effects["audio"].Values["master"] > 0;
        var gain = enabled ? settings.Effects["audio"].Values["master"] / 100 : 0;
        if (settings.Audio.DuckingEnabled && (videos.IsPlaying || KeywordDucking || FlashDucking)) gain *= 1 - settings.Effects["audio"].Values["duck"] / 100;
        drainGate.Update(enabled, gain);
        if (!enabled) { drainTicket = null; drainAudioNotified = false; return; }
        if (drainTicket is { } ticket)
        {
            if (!drainAudioNotified && ticket.PlayedSeconds > 0)
            { drainAudioNotified = true; DeviceEvent?.Invoke(LocalDeviceEvent.BrainDrain); }
            if (ticket.IsFinished) { drainTicket = null; drainAudioNotified = false; }
        }
        if (elapsed < drainNextRoll) return;
        drainNextRoll = elapsed + interval;
        var probability = settings.Effects["drain"].Values["intensity"] / 100 * interval / 60;
        if (drainTicket is not null || drainRandom.NextDouble() >= probability) return;
        if (drainScanFailed) return;
        if (drainScan?.IsCompletedSuccessfully != true || drainScan.Result.Audio.Count == 0) return;
        var files = drainScan.Result.Audio;
        drainTicket = drainGate.PlayTracked(files[drainRandom.Next(files.Count)], 1);
    }
    private void StopBrainDrain()
    {
        drainGate?.Stop(); drainTicket = null; drainAudioNotified = false;
        drainScanStop?.Cancel(); drainScanStop?.Dispose(); drainScanStop = null; drainScan = null;
        StopBrainDrainCapture(); drainHoldUntil = drainFade = drainFadeFrom = drainFadeTarget = drainFadeAt = 0; drainStatus = null; drainScanFailed = false;
    }
    private void DisposeBrainDrain() { StopBrainDrain(); drainAudio.Dispose(); }
}
