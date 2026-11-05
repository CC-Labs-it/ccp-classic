using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

// Native adaptation of public CCP compositor FlashLayer exit transforms, slices and sparks at fe40ea7.
public sealed partial class VisualEffectsControl
{
    private static readonly BoxShadows FlashHalo = new(new BoxShadow
    { Blur = 18, Color = Color.FromArgb(128, 255, 105, 180) });
    private static void DrawFlashFrame(DrawingContext context, Rect bounds, FlashFrame flash)
    {
        if (flash.Shatter is { } broken)
        {
            var sx = bounds.Width / Math.Max(1, broken.BoundsW); var sy = bounds.Height / Math.Max(1, broken.BoundsH);
            var pivot = new Point(bounds.X + broken.PivotX * sx, bounds.Y + broken.PivotY * sy);
            using var tilt = context.PushTransform(Matrix.CreateTranslation(-pivot.X, -pivot.Y)
                * Matrix.CreateRotation(broken.FrozenAngleRad) * Matrix.CreateTranslation(pivot.X, pivot.Y));
            foreach (var shard in broken.Shards)
            {
                if (shard.Alpha <= 0) continue;
                var cut = new Rect(shard.U0 * flash.Image.Size.Width, shard.V0 * flash.Image.Size.Height,
                    (shard.U1 - shard.U0) * flash.Image.Size.Width, (shard.V1 - shard.V0) * flash.Image.Size.Height);
                var target = new Rect(bounds.X + (broken.RectX + shard.U0 * broken.RectW + shard.Dx) * sx,
                    bounds.Y + (broken.RectY + shard.V0 * broken.RectH + shard.Dy) * sy,
                    (shard.U1 - shard.U0) * broken.RectW * sx, (shard.V1 - shard.V0) * broken.RectH * sy);
                using (context.PushOpacity(Alpha(flash.Opacity * shard.Alpha)))
                using (context.PushTransform(Matrix.CreateTranslation(-target.Center.X, -target.Center.Y)
                    * Matrix.CreateRotation(shard.AngleRad) * Matrix.CreateTranslation(target.Center.X, target.Center.Y)))
                    context.DrawImage(flash.Image, cut, target);
            }
            return;
        }
        var center = new Point(bounds.X + flash.Bounds.Center.X * bounds.Width, bounds.Y + flash.Bounds.Center.Y * bounds.Height);
        using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(flash.Rotation)
            * Matrix.CreateTranslation(center.X, center.Y)))
            DrawFlash(context, bounds, flash.Image, flash.Bounds, flash.Opacity, flash.Exit, flash.RoundedCorners, flash.Glow);
    }
    private static void DrawFlash(DrawingContext context, Rect bounds, IImage image, Rect box,
        double opacity, FlashExitState? exit = null, bool rounded = true, bool glow = false)
    {
        if (Alpha(opacity) <= 0 || !double.IsFinite(box.X + box.Y + box.Width + box.Height)
            || box.Width <= 0 || box.Height <= 0 || image.Size.Width <= 0 || image.Size.Height <= 0) return;
        var target = new Rect(box.X * bounds.Width, box.Y * bounds.Height, box.Width * bounds.Width, box.Height * bounds.Height);
        var scale = Math.Max(target.Width / image.Size.Width, target.Height / image.Size.Height);
        var width = target.Width / scale; var height = target.Height / scale;
        var source = new Rect((image.Size.Width - width) / 2, (image.Size.Height - height) / 2, width, height);
        var pose = exit is null ? new FlashExitSample(1, 1, 0, .5, 0, 1, 0, 0) : FlashExit.Sample(exit);
        var pivot = new Point(target.Center.X, target.Y + target.Height * pose.PivotY);
        var transform = Matrix.CreateTranslation(-pivot.X, -pivot.Y)
            * Matrix.CreateScale(Math.Max(.0001, pose.ScaleX), Math.Max(.0001, pose.ScaleY))
            * Matrix.CreateRotation(pose.RotationDeg * Math.PI / 180)
            * Matrix.CreateTranslation(pivot.X, pivot.Y + pose.OffsetY * target.Height);
        if (glow)
            using (context.PushTransform(transform))
            using (context.PushOpacity(Alpha(opacity * pose.Alpha)))
                context.DrawRectangle(Brushes.Transparent, null, target, rounded ? 12 : 0, rounded ? 12 : 0, FlashHalo);
        using (context.PushTransform(transform))
        using (context.PushRenderOptions(new RenderOptions { RequiresFullOpacityHandling = true }))
        using (context.PushOpacity(Alpha(opacity * pose.Alpha)))
        using (context.PushClip(new RoundedRect(target, rounded ? 8 : 0)))
        {
            if (exit is not null && pose.Glitch > 0)
            {
                var split = pose.Glitch * .04 * target.Width;
                if (split > .5)
                {
                    Ghost(context, image, source, target.Translate(new Vector(-split, 0)), Color.FromRgb(255, 40, 90));
                    Ghost(context, image, source, target.Translate(new Vector(split, 0)), Color.FromRgb(40, 230, 255));
                }
                for (var i = 0; i < 7; i++)
                {
                    var part = new Rect(source.X, source.Y + source.Height * i / 7, source.Width, source.Height / 7);
                    var destination = new Rect(target.X + FlashExit.SliceOffset(exit, i) * target.Width,
                        target.Y + target.Height * i / 7, target.Width, target.Height / 7);
                    context.DrawImage(image, part, destination);
                }
            }
            else context.DrawImage(image, source, target);
            if (pose.Whiten > 0)
                using (context.PushOpacity(Alpha(pose.Whiten))) context.DrawRectangle(Brushes.White, null, target, 8, 8);
        }
        if (exit is null) return;
        for (var i = 0; i < FlashExit.SparkCount(exit); i++)
        {
            var spark = FlashExit.Spark(exit, i);
            if (spark.Radius <= .2 || spark.Alpha <= 0) continue;
            var half = Math.Max(target.Width, target.Height) / 2;
            var center = target.Center + new Vector(spark.Dx * spark.Distance * half, spark.Dy * spark.Distance * half);
            using (context.PushOpacity(Alpha(spark.Alpha))) context.DrawEllipse(Pink, null, center, spark.Radius, spark.Radius);
        }
    }

    private static void Ghost(DrawingContext context, IImage image, Rect source, Rect destination, Color color)
    {
        if (image is not IImageBrushSource brushSource) return;
        // Isolate the multiply tint and preserve the source alpha without extra decoded images.
        using (context.PushRenderOptions(new RenderOptions { RequiresFullOpacityHandling = true }))
        using (context.PushOpacity(.5))
        using (context.PushOpacityMask(new ImageBrush(brushSource) { Stretch = Stretch.UniformToFill }, destination))
        {
            context.DrawRectangle(new SolidColorBrush(color), null, destination);
            using (context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Multiply }))
                context.DrawImage(image, source, destination);
        }
    }
}
