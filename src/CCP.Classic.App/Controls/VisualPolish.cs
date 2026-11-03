using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.Core;

namespace CCP.Classic.App.Controls;

// Shared CCP chrome behavior. Idle controls have no active animation clock.
public static class VisualPolish
{
    private static readonly ConditionalWeakTable<Control, Action> HoverBindings = new();
    private static readonly ConditionalWeakTable<Window, object> WindowBindings = new();
    private static WindowIcon? applicationIcon;
    public static bool MotionAllowed(ClassicSettings settings) => !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode;

    public static void ApplyWindow(Window window, ClassicSettings settings)
    {
        if (MotionAllowed(settings)) { if (!window.Classes.Contains("motion")) window.Classes.Add("motion"); }
        else
        {
            window.Classes.Remove("motion");
            foreach (var control in window.GetVisualDescendants().OfType<Control>())
                if (HoverBindings.TryGetValue(control, out var reset)) reset();
        }
        if (window.Icon is null)
        {
            if (applicationIcon is null)
            {
                using var icon = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/app.ico"));
                applicationIcon = new WindowIcon(icon);
            }
            window.Icon = applicationIcon;
        }
        BindButtons(window, settings);
        foreach (var child in window.OwnedWindows.Where(child => WindowBindings.TryGetValue(child, out _))) ApplyWindow(child, settings);
        if (WindowBindings.TryGetValue(window, out _)) return;
        WindowBindings.Add(window, new());
        window.Opened += (_, _) => BindButtons(window, settings);
    }

    private static void BindButtons(Control root, ClassicSettings settings)
    {
        foreach (var button in root.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("pill") || button.Classes.Contains("primary")))
            AttachHover(button, settings, 1.025);
    }

    public static void Enter(Control control, ClassicSettings settings)
    {
        BindButtons(control, settings);
        if (!MotionAllowed(settings)) return;
        var watch = Stopwatch.StartNew(); var shift = new TranslateTransform(0, 7);
        control.RenderTransform = shift; control.Opacity = 0;
        DispatcherTimer? timer = null;
        void Finish() { timer?.Stop(); control.Opacity = 1; control.RenderTransform = null; }
        timer = new(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            if (!control.IsAttachedToVisualTree() || !MotionAllowed(settings)) { Finish(); return; }
            var t = Math.Clamp(watch.Elapsed.TotalSeconds / .24, 0, 1); var eased = 1 - Math.Pow(1 - t, 3);
            control.Opacity = eased; shift.Y = 7 * (1 - eased);
            if (t == 1) Finish();
        });
        control.DetachedFromVisualTree += (_, _) => Finish(); timer.Start();
    }

    public static void AttachHover(Control control, ClassicSettings settings, double hoverScale = 1.02)
    {
        if (HoverBindings.TryGetValue(control, out _)) return;
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform();
        control.RenderTransformOrigin = RelativePoint.Center;
        control.RenderTransform = new TransformGroup { Children = [scale, shift] };
        var hovered = false; var pressed = false; var value = 0d;
        var watch = Stopwatch.StartNew(); var previous = watch.Elapsed.TotalSeconds;
        DispatcherTimer? timer = null;
        void Reset() { timer?.Stop(); value = 0; scale.ScaleX = scale.ScaleY = 1; shift.Y = 0; }
        HoverBindings.Add(control, Reset);
        void Step()
        {
            if (!control.IsAttachedToVisualTree() || !MotionAllowed(settings)) { Reset(); return; }
            var now = watch.Elapsed.TotalSeconds; var dt = Math.Clamp(now - previous, 0, .05); previous = now;
            var target = pressed ? -1 : hovered ? 1 : 0;
            value += (target - value) * (1 - Math.Exp(-dt / .065));
            if (Math.Abs(value - target) < .002) { value = target; timer?.Stop(); }
            scale.ScaleX = scale.ScaleY = value >= 0 ? 1 + (hoverScale - 1) * value : 1 + .025 * value;
            shift.Y = value >= 0 ? -2 * value : -value;
        }
        void Wake() { previous = watch.Elapsed.TotalSeconds; if (MotionAllowed(settings)) timer?.Start(); else Reset(); }
        timer = new(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        control.PointerEntered += (_, _) => { hovered = true; Wake(); };
        control.PointerExited += (_, _) => { hovered = false; pressed = false; Wake(); };
        control.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        { if (args.GetCurrentPoint(control).Properties.IsLeftButtonPressed) { pressed = true; Wake(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        control.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { pressed = false; Wake(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        control.PointerCaptureLost += (_, _) => { pressed = false; Wake(); };
        control.DetachedFromVisualTree += (_, _) => Reset();
    }
}
