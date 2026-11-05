using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed record FlashFrame(IImage Image, Rect Bounds, double Opacity, FlashExitState? Exit = null)
{
    public double Rotation { get; init; }
    public CCP.Classic.Core.Flash.FlashShatterState? Shatter { get; init; }
    public bool RoundedCorners { get; init; } = true;
    public bool Glow { get; init; }
    public Rect InputBounds { get; init; } = Bounds;
}

// Final opacities and phase come from the shared engine clock. Images remain caller-owned.
public sealed record EffectFrame
{
    public static EffectFrame Empty { get; } = new();
    public double SpiralPhase { get; init; }
    public int SpiralKind { get; init; }
    public IImage? CustomSpiral { get; init; }
    public double SpiralOpacity { get; init; }
    public double PinkOpacity { get; init; }
    public Color PinkColour { get; init; } = Color.Parse("#FF69B4");
    public bool PinkUnderEffects { get; init; }
    public IImage? FlashImage { get; init; }
    public Rect FlashBounds { get; init; } = new(.25, .25, .5, .5);
    public double FlashOpacity { get; init; }
    public IReadOnlyList<FlashFrame> Flashes { get; init; } = [];
    public string? SubliminalText { get; init; }
    public string SubliminalFont { get; init; } = "Arial";
    public Color SubliminalColour { get; init; } = Color.Parse("#FF69B4");
    public double SubliminalOpacity { get; init; }
    public bool ReducedMotion { get; init; }
    public InteractiveEffectsFrame Interactive { get; init; } = InteractiveEffectsFrame.Empty;
    public IReadOnlyList<IImage> BubbleImages { get; init; } = [];
    public bool GentleVisuals { get; init; }
    public bool PerformanceMode { get; init; }
}

public sealed partial class VisualEffectsControl : Control
{
    private static readonly Lazy<StreamGeometry[]> Spirals = new(() => [Spiral(3), Spiral(5), Spiral(8)]);
    private static readonly IBrush Pink = new SolidColorBrush(Color.Parse("#FF69B4"));
    private static readonly Pen SpiralEdge = new(new SolidColorBrush(Color.Parse("#D9FFFFFF")), .004);
    // Same eight outline offsets as CCP's compositor SubliminalLayer, MIT.
    private static readonly (double X, double Y)[] Outline =
        [(-3, -3), (3, -3), (-3, 3), (3, 3), (0, -4), (0, 4), (-4, 0), (4, 0)];
    private (string Text, Color Colour, double Width, string Font)? textKey;
    private FormattedText? words, outline;
    public EffectFrame Frame { get; private set; } = EffectFrame.Empty;

    public VisualEffectsControl() => IsHitTestVisible = false;

    public void UpdateFrame(EffectFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Frame == frame) return;
        Frame = frame;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using (context.PushClip(bounds))
        {
            if (Frame.PinkUnderEffects) DrawPink(context, bounds);
            if (Frame.FlashImage is { } image) DrawFlash(context, bounds, image, Frame.FlashBounds, Frame.FlashOpacity);
            foreach (var flash in Frame.Flashes) DrawFlashFrame(context, bounds, flash);
            DrawSubliminal(context, bounds);
            DrawSpiral(context, bounds);
            if (!Frame.PinkUnderEffects) DrawPink(context, bounds);
            DrawInteractive(context, bounds);
        }
    }

    private void DrawPink(DrawingContext context, Rect bounds)
    {
        if (Alpha(Frame.PinkOpacity) > 0)
            using (context.PushOpacity(Alpha(Frame.PinkOpacity)))
                context.DrawRectangle(new SolidColorBrush(Frame.PinkColour), null, bounds);
    }

    private void DrawInteractive(DrawingContext context, Rect bounds)
    {
        BubbleVisualRenderer.Draw(context, bounds, Frame.Interactive.ImageVisuals, Frame.BubbleImages);
        foreach (var bubble in Frame.Interactive.Bubbles)
            BubbleRenderer.Draw(context, bounds, bubble,
                bubble.Image >= 0 && bubble.Image < Frame.BubbleImages.Count ? Frame.BubbleImages[bubble.Image] : null,
                Frame.SpiralPhase, Frame.ReducedMotion, Frame.GentleVisuals, Frame.PerformanceMode);
        foreach (var bounce in Frame.Interactive.BouncingTexts) BouncingTextRenderer.Draw(context, bounds, bounce);
    }

    private void DrawSubliminal(DrawingContext context, Rect bounds)
    {
        if (string.IsNullOrWhiteSpace(Frame.SubliminalText) || Alpha(Frame.SubliminalOpacity) <= 0) return;
        var width = Math.Max(1, bounds.Width * .9);
        var key = (Frame.SubliminalText, Frame.SubliminalColour, width, Frame.SubliminalFont);
        if (textKey != key)
        {
            var face = new Typeface(CCP.Classic.App.Controls.LocalFontPicker.Resolve(Frame.SubliminalFont), FontStyle.Normal, FontWeight.Bold);
            words = MakeText(key.Item1, face, new SolidColorBrush(key.Item2), width);
            outline = MakeText(key.Item1, face, Brushes.Black, width);
            textKey = key;
        }
        var origin = new Point((bounds.Width - width) / 2, (bounds.Height - words!.Height) / 2);
        // Composite the outline and face once. Per-draw alpha would accumulate across nine overlapping copies.
        using (context.PushClip(new Rect(origin, new Size(width, words.Height)).Inflate(4)))
        using (context.PushRenderOptions(new RenderOptions { RequiresFullOpacityHandling = true }))
        using (context.PushOpacity(Alpha(Frame.SubliminalOpacity)))
        {
            foreach (var (x, y) in Outline) context.DrawText(outline!, origin + new Vector(x, y));
            context.DrawText(words, origin);
        }
    }

    private void DrawSpiral(DrawingContext context, Rect bounds)
    {
        if (Alpha(Frame.SpiralOpacity) <= 0) return;
        if (Frame.CustomSpiral is { } image)
        {
            var scale = Math.Max(bounds.Width / image.Size.Width, bounds.Height / image.Size.Height);
            var source = new Rect((image.Size.Width - bounds.Width / scale) / 2,
                (image.Size.Height - bounds.Height / scale) / 2, bounds.Width / scale, bounds.Height / scale);
            using (context.PushOpacity(Alpha(Frame.SpiralOpacity))) context.DrawImage(image, source, bounds);
            return;
        }
        var radius = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2;
        var phase = double.IsFinite(Frame.SpiralPhase) ? Frame.SpiralPhase % Math.Tau : 0;
        // Reduced motion is resolved by the caller freezing phase, preserving the current pose.
        var transform = Matrix.CreateRotation(phase) * Matrix.CreateScale(radius, radius)
            * Matrix.CreateTranslation(bounds.Width / 2, bounds.Height / 2);
        using (context.PushOpacity(Alpha(Frame.SpiralOpacity)))
        using (context.PushTransform(transform))
            context.DrawGeometry(Pink, SpiralEdge, Spirals.Value[Math.Clamp(Frame.SpiralKind, 0, 2)]);
    }

    private static FormattedText MakeText(string text, Typeface face, IBrush brush, double width) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 120, brush)
        { MaxTextWidth = width, TextAlignment = TextAlignment.Center };

    private static StreamGeometry Spiral(int arms)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        var band = Math.Tau / arms * .43;
        for (var arm = 0; arm < arms; arm++)
        {
            path.BeginFigure(new Point(), true);
            for (var i = 1; i <= 320; i++) path.LineTo(SpiralPoint(i, arm, arms, 0));
            for (var i = 320; i >= 0; i--) path.LineTo(SpiralPoint(i, arm, arms, band));
            path.EndFigure(true);
        }
        return geometry;
    }

    private static Point SpiralPoint(int index, int arm, int arms, double offset)
    {
        var radius = index / 320d * 1.08;
        var angle = arm * Math.Tau / arms + radius * Math.Tau * 2.5 + offset;
        return new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }
    private static double Alpha(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
