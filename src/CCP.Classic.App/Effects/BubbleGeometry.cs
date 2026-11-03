using Avalonia;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class BubbleGeometry
{
    public static Rect NormalizedBounds(BubbleCue bubble, double viewportWidth, double viewportHeight)
    {
        var bounds = BaseBounds(bubble, viewportWidth, viewportHeight);
        if (bounds.Width <= 0 || bubble.Animation is not { } animation) return bounds;
        var scale = double.IsFinite(animation.Scale) ? Math.Clamp(animation.Scale, .05, 2) : 1;
        return new Rect(bounds.Center.X - bounds.Width * scale / 2, bounds.Center.Y - bounds.Height * scale / 2,
            bounds.Width * scale, bounds.Height * scale);
    }

    public static Rect BaseBounds(BubbleCue bubble, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0
            || !double.IsFinite(bubble.X) || !double.IsFinite(bubble.Y) || !double.IsFinite(bubble.Size) || bubble.Size <= 0) return default;
        var diameter = bubble.DiameterDip > 0 ? Math.Clamp(bubble.DiameterDip, 60, 500) : Math.Min(viewportWidth, viewportHeight) * Math.Min(1, bubble.Size);
        var width = diameter / viewportWidth; var height = diameter / viewportHeight;
        var x = bubble.X; var y = bubble.Y;
        if (bubble.Animation is { ReferenceWidth: > 0, ReferenceHeight: > 0 } motion)
        {
            x += motion.SwayDip * (1 / viewportWidth - 1 / motion.ReferenceWidth);
            y += motion.RiseDip * (1 / motion.ReferenceHeight - 1 / viewportHeight);
        }
        if (diameter <= 0 || x >= 1 || y >= 1 || x + width <= 0 || y + height <= 0) return default;
        return new(x, y, width, height);
    }

    public static bool HitTest(BubbleCue bubble, Point normalized, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(normalized.X) || !double.IsFinite(normalized.Y)
            || normalized.X < 0 || normalized.Y < 0 || normalized.X > 1 || normalized.Y > 1) return false;
        var bounds = NormalizedBounds(bubble, viewportWidth, viewportHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        var dx = (normalized.X - bounds.Center.X) / (bounds.Width / 2);
        var dy = (normalized.Y - bounds.Center.Y) / (bounds.Height / 2);
        return dx * dx + dy * dy <= 1;
    }
}
