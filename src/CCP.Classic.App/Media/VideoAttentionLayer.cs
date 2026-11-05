using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

internal sealed class VideoAttentionLayer : Control
{
    private readonly Dictionary<long, Rect> hitBounds = [];
    private readonly Dictionary<(string, string, int, string), (FormattedText Text, Geometry? Geometry)> textCache = [];
    private IReadOnlyList<VideoAttentionTarget> targets = [];
    private VideoOptions options = new();
    private double elapsed;
    private bool gentle;
    public event Action<long>? HitRequested;
    public VideoAttentionLayer()
    {
        Name = "VideoAttentionTargets"; ClipToBounds = true;
        PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var point = args.GetPosition(this);
            foreach (var pair in hitBounds.Reverse())
                if (pair.Value.Contains(point)) { HitRequested?.Invoke(pair.Key); args.Handled = true; break; }
        };
    }
    public void Update(IReadOnlyList<VideoAttentionTarget> targets, VideoOptions options, double elapsed, bool gentle)
    { this.targets = targets; this.options = options; this.elapsed = elapsed; this.gentle = gentle; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        hitBounds.Clear();
        using (context.PushClip(new Rect(Bounds.Size)))
        foreach (var target in targets)
        {
            var pieces = target.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var value = pieces.Length == 2 ? string.Join('\n', pieces) : target.Text;
            var key = (value, options.TargetFont, options.TargetSize, options.TargetTextColor);
            if (!textCache.TryGetValue(key, out var shape))
            {
                if (textCache.Count >= 16) textCache.Clear();
                var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(LocalFontPicker.Resolve(options.TargetFont), FontStyle.Normal, FontWeight.Bold),
                    options.TargetSize, Brush.Parse(options.TargetTextColor)) { TextAlignment = TextAlignment.Center };
                text.MaxTextWidth = text.Width;
                shape = (text, text.BuildGeometry(default)); textCache[key] = shape;
            }
            var width = Math.Min(Bounds.Width, Math.Max(150, shape.Text.Width + 75));
            var height = Math.Min(Bounds.Height, Math.Max(60, shape.Text.Height + 55));
            var marginX = Math.Min(40, Bounds.Width * .08); var marginY = Math.Min(30, Bounds.Height * .08);
            var x = Math.Min(marginX, (Bounds.Width - width) / 2) + target.X * Math.Max(0, Bounds.Width - width - marginX * 2);
            var y = Math.Min(marginY, (Bounds.Height - height) / 2) + target.Y * Math.Max(0, Bounds.Height - height - marginY * 2);
            var rect = new Rect(x, y, Math.Max(1, width), Math.Max(1, height)); hitBounds[target.Id] = rect;
            using (context.PushOpacity(target.Opacity * Math.Clamp((elapsed - target.Spawned) / (gentle ? .75 : .25), 0, 1)))
            {
                if (!options.TargetFloatingText)
                {
                    var gradient = new LinearGradientBrush { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
                        GradientStops = [new(Color.Parse(options.TargetColor1), 0), new(Color.Parse(options.TargetColor2), 1)] };
                    context.DrawRectangle(gradient, options.TargetShowBorder ? new Pen(Brush.Parse(options.TargetBorderColor), 3) : null, rect, 20, 20);
                }
                var origin = rect.Center - new Vector(shape.Text.Width / 2, shape.Text.Height / 2);
                using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                {
                    if (shape.Geometry is { } geometry) context.DrawGeometry(null, new Pen(Brushes.Black, 7.5), geometry);
                    context.DrawText(shape.Text, default);
                }
            }
        }
    }
    internal Rect TargetBounds(long id) => hitBounds.GetValueOrDefault(id);
    public static (double Width, double Height) Measure(string value, VideoOptions options)
    {
        var pieces = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 2) value = string.Join('\n', pieces);
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(LocalFontPicker.Resolve(options.TargetFont), FontStyle.Normal, FontWeight.Bold), options.TargetSize, Brushes.White);
        return (Math.Max(150, text.Width + 75), Math.Max(60, text.Height + 55));
    }
}
