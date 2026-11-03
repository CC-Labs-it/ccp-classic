using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Controls;

public sealed class ToyPatternPreview : Control
{
    private IReadOnlyList<LocalHapticStep> steps = [];
    public ToyPatternPreview() { Height = 64; MinWidth = 200; IsHitTestVisible = false; }
    public void Configure(LocalHapticPattern pattern, double intensity, int milliseconds, string temperament)
    {
        var profile = LocalHapticTemperament.FromKey(temperament);
        steps = LocalHapticPatterns.Render(pattern, intensity, TimeSpan.FromMilliseconds(milliseconds), seed: 17)
            .Select(step => profile.Shape(step) with { Intensity = Math.Clamp(step.Intensity * profile.TransientScale, 0, 1) }).ToArray();
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size).Deflate(4); if (area.Width <= 0 || area.Height <= 0) return;
        context.DrawRectangle(Brush.Parse("#171022"), null, new Rect(Bounds.Size), 6, 6);
        var grid = new Pen(Brush.Parse("#442D52"), 1);
        for (var index = 1; index < 4; index++) context.DrawLine(grid, new(area.X, area.Y + area.Height * index / 4), new(area.Right, area.Y + area.Height * index / 4));
        var duration = Math.Max(1, LocalHapticPatterns.TotalMilliseconds(steps)); var curve = new StreamGeometry();
        using (var path = curve.Open())
        {
            for (var index = 0; index <= 200; index++)
            {
                var level = LocalHapticPatterns.Sample(steps, index * duration / 200d);
                var point = new Point(area.X + area.Width * index / 200, area.Bottom - area.Height * level);
                if (index == 0) path.BeginFigure(point, false); else path.LineTo(point);
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brush.Parse("#FF91CE"), 2), curve);
    }
}
