using Avalonia;
using Avalonia.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal static class BubbleVisualRenderer
{
    public static void Draw(DrawingContext context, Rect bounds, IReadOnlyList<BubbleImageVisual> visuals, IReadOnlyList<IImage> images)
    {
        using (context.PushClip(bounds))
        foreach (var cue in visuals)
        {
            if (cue.Image < 0 || cue.Image >= images.Count || cue.Opacity <= 0) continue;
            var image = images[cue.Image]; if (image.Size.Width <= 0 || image.Size.Height <= 0) continue;
            using (context.PushOpacity(Math.Clamp(cue.Opacity, 0, 1)))
            {
                if (cue.Fullscreen)
                {
                    var fit = Math.Max(bounds.Width / image.Size.Width, bounds.Height / image.Size.Height);
                    var crop = new Rect((image.Size.Width - bounds.Width / fit) / 2, (image.Size.Height - bounds.Height / fit) / 2,
                        bounds.Width / fit, bounds.Height / fit);
                    context.DrawImage(image, crop, bounds);
                }
                else
                {
                    var width = cue.SizeDip; var height = width * image.Size.Height / image.Size.Width;
                    var x = bounds.X + cue.X * Math.Max(0, bounds.Width - width); var y = bounds.Y + cue.YDip;
                    var centre = new Point(x + width / 2, y + height / 2);
                    var rect = new Rect(centre.X - width * cue.Scale / 2, centre.Y - height * cue.Scale / 2, width * cue.Scale, height * cue.Scale);
                    context.DrawImage(image, rect);
                }
            }
        }
    }
}
