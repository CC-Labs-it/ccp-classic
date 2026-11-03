using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace CCP.Classic.App.Controls;

// The original layered CCP logo, rendered one frame at a time off the UI thread.
public sealed class AnimatedLogo : Control
{
    private static readonly Uri Artwork = new("avares://CCP.Classic.App/Assets/logo.jpg");
    private readonly Func<bool> motionAllowed;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Bitmap? still;
    private DashboardLogoRenderer? renderer;
    private WriteableBitmap? frame;
    private SKBitmap? buffer;
    private Task? pending;
    private bool attached;
    private double phase, energy, last;
    public bool IsMotionActive => attached && motionAllowed();
    internal long PresentedFrames { get; private set; }

    public AnimatedLogo(Func<bool> motionAllowed)
    {
        this.motionAllowed = motionAllowed;
        Name = "AnimatedLogo";
        using var stream = AssetLoader.Open(Artwork);
        still = new Bitmap(stream);
        timer = new(TimeSpan.FromSeconds(1d / 30), DispatcherPriority.Background, (_, _) => Step());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); attached = true;
        if (still is null) { using var art = AssetLoader.Open(Artwork); still = new Bitmap(art); }
        last = clock.Elapsed.TotalSeconds; timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false; timer.Stop(); frame?.Dispose(); frame = null; still?.Dispose(); still = null;
        var oldRenderer = renderer; var oldBuffer = buffer; renderer = null; buffer = null;
        void Free() { oldRenderer?.Dispose(); oldBuffer?.Dispose(); }
        if (pending is { } draw) _ = draw.ContinueWith(_ => Free(), TaskScheduler.Default);
        else Free();
        base.OnDetachedFromVisualTree(e);
    }

    private void Step()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - last, 0, .1); last = now;
        if (!IsMotionActive) { if (frame is not null) { frame.Dispose(); frame = null; InvalidateVisual(); } return; }
        energy += ((IsPointerOver ? 1 : 0) - energy) * (1 - Math.Exp(-dt / .3));
        phase = (phase + dt * Math.Tau / 12 * (1 + 2 * energy)) % Math.Tau;
        if (pending is not null || Bounds.Width < 1) return;
        if (renderer is null) { using var art = AssetLoader.Open(Artwork); renderer = new(art); }
        var size = Math.Clamp((int)(Math.Min(Bounds.Width, Bounds.Height) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)), 128, 512);
        if (buffer?.Width != size) { buffer?.Dispose(); buffer = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul); }
        var target = buffer; var painter = renderer; var position = phase; var drive = .35 + .65 * energy;
        pending = Task.Run(() =>
        {
            using var canvas = new SKCanvas(target); painter.Draw(canvas, size, size, position, drive);
        });
        _ = pending.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            pending = null;
            if (result.IsFaulted || !IsMotionActive || !ReferenceEquals(target, buffer)) return;
            if (frame?.PixelSize.Width != size)
            {
                frame?.Dispose(); frame = new(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using (var pixels = target.PeekPixels())
            using (var locked = frame.Lock()) pixels.ReadPixels(target.Info, locked.Address, locked.RowBytes);
            PresentedFrames++;
            InvalidateVisual();
        }), TaskScheduler.Default);
    }

    public override void Render(DrawingContext context)
    {
        if (still is null) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        using (context.PushClip(new RoundedRect(rect, size * 83 / 1024)))
            context.DrawImage((IImage?)frame ?? still, rect);
    }
}
