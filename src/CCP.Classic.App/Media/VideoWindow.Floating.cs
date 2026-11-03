using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Platform;
using CCP.Classic.Core.Flash;
using CCP.Classic.Core.Video;

namespace CCP.Classic.App.Media;

internal sealed partial class VideoWindow
{
    private FloatingVideoMotion? floating;
    private double floatingAt;
    private IPointer? captured;
    internal Screen? TargetScreen { get; set; }
    private void InitializeFloatingInput()
    {
        PointerPressed += (_, args) =>
        {
            if (!cue.Floating || completed || floating is null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (args.Source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            var p = this.PointToScreen(args.GetPosition(this)); Touched?.Invoke();
            floating.Press(p.X, p.Y, Environment.TickCount64); captured = args.Pointer; args.Pointer.Capture(this);
        };
        PointerMoved += (_, args) =>
        {
            if (floating?.Held != true || args.Pointer != captured) return;
            var p = this.PointToScreen(args.GetPosition(this)); floating.Move(p.X, p.Y, Environment.TickCount64);
        };
        PointerReleased += (_, args) =>
        {
            if (floating?.Held != true || args.Pointer != captured) return;
            var p = this.PointToScreen(args.GetPosition(this)); floating.Move(p.X, p.Y, Environment.TickCount64);
            floating.Release(Environment.TickCount64, settings.ReducedMotion ? MotionLevel.Off : settings.GentleVisuals ? MotionLevel.Reduced : MotionLevel.Full);
            captured = null; args.Pointer.Capture(null);
        };
        PointerCaptureLost += (_, _) => { if (floating?.Held == true) floating.Drop(); captured = null; };
    }
    private void PlaceFloating(Screen screen)
    {
        var area = screen.WorkingArea; var scale = screen.Scaling;
        Width = Math.Min(area.Width / scale, Math.Max(240, area.Width / scale * settings.Video.FloatingSizePercent / 100d));
        Height = Width * 9 / 16;
        if (Height > area.Height / scale) { Height = area.Height / scale; Width = Height * 16 / 9; }
        var random = new Random((int)cue.Id);
        var x = area.X + (area.Width - Width * scale) * (.08 + random.NextDouble() * .84);
        var y = area.Y + (area.Height - Height * scale) * (.08 + random.NextDouble() * .84);
        Position = new((int)x, (int)y);
        floating = new(x, y, Width * scale, Height * scale, area.X, area.Y, area.Width, area.Height)
        {
            Speed = settings.Video.FloatingSpeedPercent / 100d,
            MonitorAt = (cx, cy) =>
            {
                var next = Screens.ScreenFromPoint(new PixelPoint((int)cx, (int)cy));
                return next is null ? null : (next.WorkingArea.X, next.WorkingArea.Y, next.WorkingArea.Width, next.WorkingArea.Height);
            }
        };
        if (Screens.All.Count > 0)
        {
            var left = Screens.All.Min(s => s.Bounds.X); var top = Screens.All.Min(s => s.Bounds.Y);
            var right = Screens.All.Max(s => s.Bounds.Right); var bottom = Screens.All.Max(s => s.Bounds.Bottom);
            floating.SetDesktop(left, top, right - left, bottom - top);
        }
        floatingAt = lastNow;
    }
    private void StepFloating(double now)
    {
        if (floating is null) return;
        if (settings.ReducedMotion && !floating.Held) floating.Drop();
        floating.Speed = settings.Video.FloatingSpeedPercent / 100d;
        if (floating.Step(Math.Clamp(now - floatingAt, 0, .1))) Position = new((int)floating.X, (int)floating.Y);
        floatingAt = now;
    }
}
