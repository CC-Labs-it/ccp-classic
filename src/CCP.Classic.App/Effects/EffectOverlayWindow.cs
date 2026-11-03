using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;

namespace CCP.Classic.App.Effects;

public sealed class EffectOverlayWindow : Window
{
    private NativeOverlayInput? input;
    private bool prepared;
    public VisualEffectsControl View { get; } = new();
    public string? LastError { get; private set; }
    public event Action<Point>? FlashPressed;
    public Func<Point, bool>? TryBeginDrag { get; set; }
    public event Action<Point>? DragMoved;
    public event Action? DragFinished;
    private bool dragging;

    public EffectOverlayWindow(Action<Window>? configureInput = null)
    {
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = new Border { Background = Brushes.Transparent, Child = View };
        configureInput?.Invoke(this);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            var point = e.GetPosition(this);
            if (TryBeginDrag?.Invoke(new Point(point.X / ClientSize.Width, point.Y / ClientSize.Height)) == true)
            { dragging = true; e.Pointer.Capture(this); e.Handled = true; return; }
            FlashPressed?.Invoke(new Point(point.X / ClientSize.Width, point.Y / ClientSize.Height));
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerMoved += (_, e) =>
        {
            if (!dragging || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            var point = e.GetPosition(this); DragMoved?.Invoke(new(point.X / ClientSize.Width, point.Y / ClientSize.Height));
        };
        PointerReleased += (_, e) => { if (dragging) { dragging = false; DragFinished?.Invoke(); e.Pointer.Capture(null); e.Handled = true; } };
        PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; DragFinished?.Invoke(); } };
        PropertyChanged += (_, e) =>
        {
            if (prepared && e.Property == ActualTransparencyLevelProperty && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
                Fail("Desktop transparency is no longer available.");
        };
        Closed += (_, _) => { prepared = false; input?.Dispose(); input = null; View.UpdateFrame(EffectFrame.Empty); };
    }

    // The first map is an empty, offscreen pixel. Unsupported backends never map a desktop-sized blocker.
    public bool Prepare(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        try
        {
            prepared = false;
            View.UpdateFrame(EffectFrame.Empty);
            Position = new PixelPoint(-32000, -32000); Width = Height = 1;
            input?.Dispose(); input = null;
            Show();
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent) return Fail("Desktop transparency is unavailable.");
            Hide();
            input = NativeOverlayInput.Create(this, out var error);
            if (input is null) return Fail(error ?? "Desktop input pass-through is unavailable.");
            Position = screen.Bounds.Position;
            var scale = DesktopScaling > 0 ? DesktopScaling : screen.Scaling;
            Width = screen.Bounds.Width / scale; Height = screen.Bounds.Height / scale;
            if (!input.SetBounds([], screen.Bounds.Size)) return Fail(input.LastError);
            Show();
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent) return Fail("Desktop transparency is unavailable.");
            if (!input.SetBounds([], PixelSize.FromSize(ClientSize, RenderScaling), force: true)) return Fail(input.LastError);
            prepared = true; LastError = null;
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        { return Fail(e.Message); }
    }

    public bool SetInputBounds(IReadOnlyList<Rect> flashBounds)
    {
        if (!prepared || input is null) return false;
        if (input.SetBounds(flashBounds, PixelSize.FromSize(ClientSize, RenderScaling))) return true;
        return Fail(input.LastError);
    }

    private bool Fail(string? error)
    {
        LastError = error ?? "Desktop input pass-through failed.";
        prepared = false; View.UpdateFrame(EffectFrame.Empty); Hide(); input?.Dispose(); input = null;
        return false;
    }
}
