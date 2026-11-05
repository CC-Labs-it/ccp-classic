using Avalonia;
using Avalonia.Media;

namespace CCP.Classic.App.Controls.HelpLoops;

// Keeps the public source's drawing order and geometry while using Avalonia resources exclusively.
internal sealed class LoopContext(DrawingContext context) : IDisposable
{
    private readonly Stack<DrawingContext.PushedState> states = new();
    public void PushOpacity(double value) => states.Push(context.PushOpacity(value));
    public void PushTransform(ITransform value) => PushTransform(value.Value);
    public void PushTransform(Matrix value) => states.Push(context.PushTransform(value));
    public void PushClip(Geometry value) => states.Push(context.PushGeometryClip(value));
    public void Pop() { if (states.TryPop(out var state)) state.Dispose(); }
    public void Dispose() { while (states.Count > 0) Pop(); }
    public void DrawRectangle(IBrush? brush, IPen? pen, Rect rect) => context.DrawRectangle(brush, pen, rect);
    public void DrawRoundedRectangle(IBrush? brush, IPen? pen, Rect rect, double rx, double ry) =>
        context.DrawRectangle(brush, pen, rect, rx, ry);
    public void DrawEllipse(IBrush? brush, IPen? pen, Point center, double rx, double ry) =>
        context.DrawEllipse(brush, pen, center, rx, ry);
    public void DrawGeometry(IBrush? brush, IPen? pen, Geometry geometry) => context.DrawGeometry(brush, pen, geometry);
    public void DrawLine(IPen pen, Point start, Point end) => context.DrawLine(pen, start, end);
    public void DrawText(FormattedText text, Point origin) => context.DrawText(text, origin);
    public static Matrix Scale(double sx, double sy, double cx, double cy) =>
        Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(cx, cy);
    public static Matrix Skew(double x, double y, double cx, double cy) =>
        Matrix.CreateTranslation(-cx, -cy) * new SkewTransform(x, y).Value * Matrix.CreateTranslation(cx, cy);
}

internal sealed class LoopGeometry
{
    private readonly StreamGeometry geometry = new();
    public LoopGeometryWriter Open() => new(geometry.Open());
    public void Freeze() { }
    public static implicit operator Geometry(LoopGeometry value) => value.geometry;
}

internal sealed class LoopGeometryWriter(StreamGeometryContext context) : IDisposable
{
    private bool open, close;
    public void BeginFigure(Point point, bool filled, bool closed)
    {
        if (open) context.EndFigure(close);
        context.BeginFigure(point, filled); open = true; close = closed;
    }
    public void LineTo(Point point, bool stroked, bool smooth) => context.LineTo(point, stroked);
    public void QuadraticBezierTo(Point control, Point end, bool stroked, bool smooth) => context.QuadraticBezierTo(control, end, stroked);
    public void BezierTo(Point first, Point second, Point end, bool stroked, bool smooth) => context.CubicBezierTo(first, second, end, stroked);
    public void ArcTo(Point end, Size size, double rotation, bool large, SweepDirection direction, bool stroked, bool smooth) =>
        context.ArcTo(end, size, rotation, large, direction, stroked);
    public void Dispose() { if (open) context.EndFigure(close); open = false; context.Dispose(); }
}
