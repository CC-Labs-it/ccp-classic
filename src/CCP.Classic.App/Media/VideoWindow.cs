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

internal sealed class VideoWindow : Window
{
    private readonly VideoCue cue;
    private readonly ClassicSettings settings;
    private readonly ILocalVideoPlayback playback;
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Border attention = new() { Background = Brushes.Black, IsVisible = false };
    private readonly TextBox answer = new() { Width = 100, Name = "AttentionAnswer" };
    private WriteableBitmap? bitmap;
    private bool forced, completed, counted, released, closePending, suppressed, hiddenForOwner, pendingOwnerHide;
    private Window? owner;
    private int windowIndex;
    private string? error;
    private double? firstFrame, firstPulse, ending, attentionStarted;
    private double lastNow, nextAttention = 30, excludedTime;
    private double? lastPulse;
    private double opacityFrom, opacityTarget, opacityStarted, opacityDuration, alpha;
    private int expected;
    public long CueId => cue.Id;
    public bool HasFinished => completed;
    public string? PlaybackError => error ?? playback.LastError;
    public event Action<bool>? CountAnswered;
    public event Action? VideoFinished;
    public event Action? AttentionStarted;
    public double PlayedSeconds => playback.PlayedSeconds;
    public AudioPlaybackSample? GetAudioSample() => completed || ending is not null || IsSuppressed || attentionStarted is not null
        ? null : playback.GetAudioSample();

    public VideoWindow(VideoCue cue, ClassicSettings settings, ILocalVideoPlayback playback,
        LocalizationService text, Func<bool> requestStop, Action<Window> configureInput)
    {
        this.cue = cue; this.settings = settings; this.playback = playback;
        Title = text["MandatoryVideos"]; Background = Brushes.Black;
        Width = 560; Height = 360; MinWidth = 280; MinHeight = 200;
        ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (!cue.Floating) { WindowDecorations = WindowDecorations.None; CanResize = false; }
        var grid = new Grid(); grid.Children.Add(image); grid.Children.Add(attention); Content = grid;
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
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = text["CountBubbles"], FontSize = 20 });
        panel.Children.Add(new Canvas { Width = 300, Height = 180, Name = "AttentionBubbles" });
        panel.Children.Add(answer);
        var submit = new Button { Content = text["Submit"], Name = "AttentionSubmit" };
        submit.Click += (_, _) => { if (int.TryParse(answer.Text, out var count)) AnswerCount(count); };
        panel.Children.Add(submit); attention.Child = panel;
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
    private void SyncPause() => playback.SetPaused(IsSuppressed || attentionStarted is not null);
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
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            if (cue.Floating)
            {
                var area = screen.WorkingArea; var scale = screen.Scaling;
                Width = Math.Min(560, area.Width / scale * .7); Height = Math.Min(360, area.Height / scale * .7);
                Position = new PixelPoint(area.X + Math.Max(0, (area.Width - (int)(Width * scale)) / 2 + windowIndex * 28),
                    area.Y + Math.Max(0, (area.Height - (int)(Height * scale)) / 2 + windowIndex * 28));
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
        if (lastPulse is { } previous && (suppressed || !visible)) excludedTime += now - previous;
        lastPulse = now; suppressed = !visible;
        playback.SetGain(gain); SyncPause();
        if (suppressed) return;
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
            }
            using var locked = bitmap.Lock();
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * frame.Width * 4, locked.Address + row * locked.RowBytes, frame.Width * 4);
            image.InvalidateVisual();
            if (firstFrame is null) { firstFrame = now; nextAttention = now + 30; ShowFirstFrame(); }
        }
        if (playback.IsFinished && ending is null)
        {
            ending = now;
            if (!counted && playback.LastError is null && firstFrame is not null) { counted = true; VideoFinished?.Invoke(); }
        }
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
        if (cue.Attention && firstFrame is not null && ending is null && now >= nextAttention && attentionStarted is null)
            ShowCount(now);
        if (attentionStarted is { } asked && now - asked >= 15) { CountAnswered?.Invoke(false); Resume(); }
    }

    private void ShowCount(double now)
    {
        expected = 3 + (int)((cue.Id + (long)(nextAttention / 30)) % 5);
        var canvas = (Canvas)((StackPanel)attention.Child!).Children[1]; canvas.Children.Clear();
        for (var index = 0; index < expected; index++)
        {
            var bubble = new Ellipse { Width = 30, Height = 30, Stroke = Brushes.HotPink, StrokeThickness = 2 };
            Canvas.SetLeft(bubble, 15 + index % 4 * 70); Canvas.SetTop(bubble, 20 + index / 4 * 80);
            canvas.Children.Add(bubble);
        }
        attentionStarted = now; SyncPause(); attention.IsVisible = true; answer.Text = ""; answer.Focus();
        AttentionStarted?.Invoke();
    }

    internal bool AnswerCount(int count)
    {
        if (completed || attentionStarted is null) return false;
        var correct = count == expected; CountAnswered?.Invoke(correct);
        if (correct) Resume(); else answer.Text = "";
        return correct;
    }
    private void Resume()
    {
        attentionStarted = null; attention.IsVisible = false;
        nextAttention = lastNow + 30; SyncPause();
    }
    private static double Ease(double value) { var t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private void ClearVisuals()
    {
        attentionStarted = null; attention.IsVisible = false;
        image.Source = null; bitmap?.Dispose(); bitmap = null;
    }
    private void ReleasePlayback()
    {
        if (released) return;
        released = true;
        if (owner is not null) owner.PropertyChanged -= OwnerChanged;
        playback.Dispose();
    }
    public void FinishImmediately()
    {
        if (completed) return;
        forced = completed = true; ClearVisuals(); playback.SetGain(0); Close(); ReleasePlayback();
    }
}
