using Avalonia;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class BubbleGeometry
{
    public static Rect NormalizedBounds(BubbleCue bubble, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0
            || !double.IsFinite(bubble.X) || !double.IsFinite(bubble.Y) || !double.IsFinite(bubble.Size) || bubble.Size <= 0) return default;
        var diameter = Math.Min(viewportWidth, viewportHeight) * Math.Min(1, bubble.Size);
        var width = diameter / viewportWidth; var height = diameter / viewportHeight;
        if (diameter <= 0 || bubble.X >= 1 || bubble.Y >= 1 || bubble.X + width <= 0 || bubble.Y + height <= 0) return default;
        return new(bubble.X, bubble.Y, width, height);
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
