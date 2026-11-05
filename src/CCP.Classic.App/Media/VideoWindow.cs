using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Controls.Shapes;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Media;

internal sealed partial class VideoWindow : Window
{
    private readonly VideoCue cue;
    private readonly ClassicSettings settings;
    private readonly ILocalVideoPlayback playback;
    private readonly Image image = new() { Stretch = Stretch.Uniform, Name = "VideoImage" };
    private readonly VideoAttentionLayer attention = new();
    private readonly VideoAttentionTimeline checks;
    private readonly bool ownsChecks;
    private readonly Image backgroundImage = new() { Stretch = Stretch.UniformToFill, Opacity = .35, IsHitTestVisible = false };
    private WriteableBitmap? bitmap;
    private bool forced, completed, counted, released, closePending, suppressed, hiddenForOwner, pendingOwnerHide;
    private Window? owner;
    private int windowIndex;
    private string? error;
    private double? firstFrame, firstPulse, ending;
    private double lastNow, excludedTime;
    private double? lastPulse;
    private double opacityFrom, opacityTarget, opacityStarted, opacityDuration, alpha;
    private bool checksStarted;
    private bool attentionPassed = true, hadAttention;
    public bool AttentionPassed => ending is not null || completed ? attentionPassed : checks.Passed;
    public bool HadAttention => ending is not null || completed ? hadAttention : checks.Spawned > 0;
    public double DurationSeconds => playback.DurationSeconds;
    public event Action? Touched;
    public long CueId => cue.Id;
    public bool HasFinished => completed;
    public string? PlaybackError => error ?? playback.LastError;
    public event Action<bool>? CountAnswered;
    public event Action? VideoFinished;
    public event Action? AttentionStarted;
    public double PlayedSeconds => playback.PlayedSeconds;
    public AudioPlaybackSample? GetAudioSample() => completed || ending is not null || IsSuppressed || sound.Muted
        ? null : playback.GetAudioSample();
    public AudioPlaybackSample? GetTimelineSample() => completed || ending is not null || IsSuppressed
        ? null : playback.GetTimelineSample();

    public VideoWindow(VideoCue cue, ClassicSettings settings, ILocalVideoPlayback playback,
        LocalizationService text, Func<bool> requestStop, Action<Window> configureInput, VideoAttentionTimeline? sharedChecks = null,
        VideoSoundState? soundState = null)
    {
        this.cue = cue; this.settings = settings; this.playback = playback;
        sound = soundState ?? new();
        ownsChecks = sharedChecks is null; checks = sharedChecks ?? new((int)cue.Id);
        checks.MeasureTarget = value => VideoAttentionLayer.Measure(value, settings.Video);
        checks.Answered += CheckAnswered; checks.TargetStarted += CheckStarted;
        attention.HitRequested += id => HitTarget(id);
        Title = text["MandatoryVideos"]; Background = Brushes.Black;
        Width = 560; Height = 360; MinWidth = cue.Floating ? 240 : 280; MinHeight = cue.Floating ? 135 : 200;
        ShowInTaskbar = false; Topmost = true; ShowActivated = !cue.Floating;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (!cue.Floating) { WindowDecorations = WindowDecorations.None; CanResize = false; }
        var grid = new Grid(); grid.Children.Add(backgroundImage); grid.Children.Add(image); grid.Children.Add(attention);
        grid.Children.Add(MakeControls(text, requestStop)); Content = grid;
        InitializeFloatingInput();
        configureInput(this);
        Closing += (_, args) =>
        {
            if (forced) return;
            args.Cancel = true;
            if (!cue.Strict && !closePending)
            {
                closePending = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                { closePending = false; if (!completed) requestStop(); });
            }
        };
        Closed += (_, _) => { completed = true; ClearVisuals(); ReleasePlayback(); };
        PropertyChanged += (_, args) =>
        {
            if (completed || args.Property != IsVisibleProperty) return;
            pendingOwnerHide = firstFrame is not null && !IsVisible;
            SyncPause();
        };
    }

    public void Begin(Window owner, int index)
    {
        this.owner = owner; windowIndex = index; Opacity = 0;
        owner.PropertyChanged += OwnerChanged;
        playback.SetPaused(IsSuppressed);
        playback.Start();
    }

    private bool OwnerAvailable => owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized;
    private bool IsSuppressed => !OwnerAvailable || WindowState == WindowState.Minimized || firstFrame is not null && !IsVisible;
    private void SyncPause() { if (IsSuppressed) toyInputResumePending = true; playback.SetPaused(IsSuppressed); }
    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        // Avalonia hides owned children before changing the owner's own visibility.
        if (args.Property == IsVisibleProperty && !OwnerAvailable && pendingOwnerHide) hiddenForOwner = true;
        if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty) SyncPause();
    }

    private bool PreparePresentation()
    {
        pendingOwnerHide = false;
        if (!OwnerAvailable)
        {
            hiddenForOwner |= IsVisible;
            if (IsVisible) Hide();
            return false;
        }
        if (hiddenForOwner && WindowState != WindowState.Minimized)
        { hiddenForOwner = false; Show(owner!); }
        return !IsSuppressed;
    }

    private void ShowFirstFrame()
    {
        if (owner is null || !OwnerAvailable || completed) return;
        Show(owner);
        var screen = TargetScreen ?? owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            if (cue.Floating)
            {
                PlaceFloating(screen);
            }
            else { Position = screen.Bounds.Position; WindowState = WindowState.FullScreen; }
        }
    }

    public void Pulse(double now, double gain)
    {
        if (completed) return;
        try { UpdatePlayback(now, gain); }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or OverflowException)
        { error = failure.Message; FinishImmediately(); }
    }

    private void UpdatePlayback(double now, double gain)
    {
        now = double.IsFinite(now) ? Math.Max(lastPulse ?? now, now) : lastPulse ?? 0;
        var visible = PreparePresentation();
        if (visible && (suppressed || toyInputResumePending)) { toyInputResumedAt = now; toyInputResumePending = false; }
        if (lastPulse is { } previous && (suppressed || !visible)) excludedTime += now - previous;
        lastPulse = now; suppressed = !visible;
        lastGain = gain; playback.SetGain(sound.Muted ? 0 : gain); SyncPause();
        if (suppressed)
        {
            if (playback.IsFinished)
            {
                ObserveCompletion(lastNow);
                if (firstFrame is null) error ??= playback.LastError ?? "Video did not produce frames.";
                FinishImmediately();
            }
            return;
        }
        // Watchdogs, fades and attention use only time with an available presentation.
        now -= excludedTime;
        firstPulse ??= now;
        lastNow = now;
        if (ending is null && playback.TryCopyFrame(out var frame) && frame is not null)
        {
            if (frame.Width is <= 0 or > 960 || frame.Height is <= 0 or > 540
                || frame.Pixels.Length < frame.Width * frame.Height * 4)
                throw new InvalidOperationException("Video frame is invalid.");
            if (bitmap?.PixelSize != new PixelSize(frame.Width, frame.Height))
            {
                image.Source = null; bitmap?.Dispose();
                bitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                image.Source = bitmap;
                backgroundImage.Source = bitmap;
            }
            using var locked = bitmap.Lock();
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * frame.Width * 4, locked.Address + row * locked.RowBytes, frame.Width * 4);
            image.InvalidateVisual();
            if (firstFrame is null) { firstFrame = now; ShowFirstFrame(); }
        }
        if (playback.IsFinished && ending is null)
            ObserveCompletion(now);
        // A failed decoder or missing first frame must never leave an invisible topmost blocker.
        if (firstFrame is null && (ending is not null || now - firstPulse.Value >= 15))
        { error ??= playback.LastError ?? "Video did not produce frames."; FinishImmediately(); return; }
        var fade = settings.GentleVisuals ? .75 : .25;
        var target = firstFrame is not null && ending is null ? 1 : 0;
        if (target != opacityTarget || (settings.GentleVisuals && fade > opacityDuration && alpha != target))
        { opacityFrom = alpha; opacityTarget = target; opacityStarted = now; opacityDuration = fade; }
        alpha = opacityFrom + (opacityTarget - opacityFrom) * Ease((now - opacityStarted) / Math.Max(.001, opacityDuration));
        Opacity = alpha;
        if (ending is not null && alpha == 0) { FinishImmediately(); return; }
        backgroundImage.IsVisible = settings.Video.BlurredBackground;
        if (settings.Video.BlurredBackground && backgroundImage.Effect is null) backgroundImage.Effect = new BlurEffect { Radius = 24 };
        if (cue.Floating) StepFloating(now);
        if (cue.Attention && firstFrame is { } start && ending is null)
        {
            if (!checksStarted) { checksStarted = true;
                if (!checks.IsStarted) checks.Start(Math.Min(cue.CapSeconds, playback.DurationSeconds > 0 ? playback.DurationSeconds : cue.CapSeconds), settings.Video); }
            checks.Step(now - start, settings.Video, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), settings.GentleVisuals, settings.ReducedMotion);
            attention.Update(checks.VisualTargets, settings.Video, now - start, settings.GentleVisuals);
        }
    }

    internal IReadOnlyList<VideoAttentionTarget> Targets => checks.Targets;
    private void ObserveCompletion(double now)
    {
        if (ending is not null || completed) return;
        if (playback.LastError is null) checks.Finish();
        if (completed) return;
        attentionPassed = checks.Passed; hadAttention = checks.Spawned > 0; ending = now;
        if (!counted && playback.LastError is null && firstFrame is not null && attentionPassed)
        { counted = true; VideoFinished?.Invoke(); }
    }
    internal bool HitTarget(long id) => !completed && !IsSuppressed && ending is null && checks.Hit(id);
    private void CheckAnswered(bool correct) => CountAnswered?.Invoke(correct);
    private void CheckStarted() => AttentionStarted?.Invoke();
    private static double Ease(double value) { var t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private void ClearVisuals()
    {
        floating?.Drop(); captured?.Capture(null); captured = null;
        if (ownsChecks) checks.Clear(); attention.Update([], settings.Video, 0, settings.GentleVisuals);
        image.Source = backgroundImage.Source = null; bitmap?.Dispose(); bitmap = null;
    }
    private void ReleasePlayback()
    {
        if (released) return;
        released = true;
        checks.Answered -= CheckAnswered; checks.TargetStarted -= CheckStarted;
        sound.Changed -= SoundChanged;
        if (owner is not null) owner.PropertyChanged -= OwnerChanged;
        playback.Dispose();
    }
    public void FinishImmediately()
    {
        if (completed) return;
        forced = completed = true; ClearVisuals(); playback.SetGain(0); Close(); ReleasePlayback();
    }
}
