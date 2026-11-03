using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Effects;

// An ordinary owned window is the fallback when a desktop cannot provide safe overlays.
public sealed class InAppEffectWindow : Window, IDisposable
{
    private readonly Window owner;
    private readonly Func<bool> requestStop;
    private EffectFrame frame = EffectFrame.Empty;
    private bool forced, finished, released, closePending;
    public VisualEffectsControl View { get; } = new();
    public bool HasFinished => finished;
    public event Action<Point, Size>? InputPressed;

    public InAppEffectWindow(Window owner, LocalizationService text, Action<Window> configureInput, Func<bool> requestStop)
    {
        this.owner = owner; this.requestStop = requestStop;
        Title = text["Studio"]; Width = 640; Height = 480; MinWidth = 320; MinHeight = 240;
        MaxWidth = 960; MaxHeight = 720;
        Topmost = false; ShowInTaskbar = false; WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowDecorations = WindowDecorations.Full;
        Background = new SolidColorBrush(Color.Parse("#121220")); TransparencyBackgroundFallback = Background;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        Content = new Border { Background = Background, Child = View };
        configureInput(this);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var size = View.Bounds.Size;
            if (finished || !IsVisible || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || size.Width <= 0 || size.Height <= 0) return;
            var point = e.GetPosition(View);
            if (!double.IsFinite(point.X + point.Y) || point.X < 0 || point.Y < 0 || point.X > size.Width || point.Y > size.Height) return;
            InputPressed?.Invoke(new Point(point.X / size.Width, point.Y / size.Height), size); e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.PropertyChanged += OwnerChanged; owner.Closed += OwnerClosed;
        Closed += (_, _) => { finished = true; Clear(); Release(); };
    }
    public void UpdateFrame(EffectFrame value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (finished) return;
        frame = value; Present();
    }
    private void Present()
    {
        if (finished) return;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized || !VisibleContent(frame))
        { View.UpdateFrame(EffectFrame.Empty); if (IsVisible) Hide(); return; }
        View.UpdateFrame(frame);
        if (!IsVisible) Show(owner);
    }
    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty && e.Property != IsVisibleProperty) return;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized) Present();
        else Dispatcher.UIThread.Post(Present);
    }
    private void OwnerClosed(object? sender, EventArgs e) => FinishImmediately();
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (forced || e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        { forced = true; Clear(); }
        else
        {
            e.Cancel = true;
            if (!closePending)
            {
                closePending = true;
                Dispatcher.UIThread.Post(() =>
                {
                    closePending = false;
                    if (!finished && requestStop()) FinishImmediately();
                });
            }
        }
        base.OnClosing(e);
    }
    public void FinishImmediately()
    {
        if (finished) return;
        forced = finished = true; Clear();
        try { Close(); } finally { Release(); }
    }
    public void Dispose() => FinishImmediately();
    private void Clear() { frame = EffectFrame.Empty; View.UpdateFrame(EffectFrame.Empty); }
    private void Release()
    {
        if (released) return;
        released = true; owner.PropertyChanged -= OwnerChanged; owner.Closed -= OwnerClosed; InputPressed = null;
    }
    private static bool Alpha(double value) => double.IsFinite(value) && value > 0;
    internal static bool VisibleContent(EffectFrame value) => Alpha(value.PinkOpacity) || Alpha(value.SpiralOpacity)
        || Alpha(value.SubliminalOpacity) && !string.IsNullOrWhiteSpace(value.SubliminalText)
        || value.FlashImage is not null && Alpha(value.FlashOpacity) && ValidBox(value.FlashBounds)
        || value.Flashes.Any(flash => Alpha(flash.Opacity) && ValidBox(flash.Bounds))
        || value.Interactive.Bubbles.Any(bubble => Alpha(bubble.Opacity) && bubble.Size > 0)
        || value.Interactive.BouncingText is { } bounce && Alpha(bounce.Opacity) && !string.IsNullOrWhiteSpace(bounce.Text)
        || value.BrainDrain is { } drain && Alpha(drain.Opacity);
    private static bool ValidBox(Rect box) => double.IsFinite(box.X + box.Y + box.Width + box.Height)
        && box.Width > 0 && box.Height > 0 && box.Intersects(new Rect(0, 0, 1, 1));
}
