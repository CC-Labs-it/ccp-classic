using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Controls;

public sealed class LoomPreview : Control
{
    private readonly ClassicSettings settings;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private LoomDefinition definition = new LoomDefinition().Normalize();
    private WriteableBitmap? frame;
    private Task<SKBitmap>? pending;
    private bool attached, dirty = true, reduced, gentle, performance;
    private double phase, displayedPhase, last;
    private int generation;
    public bool Active { get; set; } = true;
    public Func<bool>? MotionAllowed { get; set; }
    public string? LastError { get; private set; }
    public double Phase => phase;
    public bool IsRenderPending => pending is not null;
    public bool HasFrame => frame is not null;
    public long RenderedFrames { get; private set; }
    public bool IsMotionActive => SurfaceActive() && !settings.ReducedMotion;
    public LoomDefinition Definition
    {
        get => definition.Normalize();
        set { definition = value.Normalize(); dirty = true; generation++; }
    }
    public LoomPreview(ClassicSettings? settings = null)
    {
        this.settings = settings ?? new();
        reduced = this.settings.ReducedMotion; gentle = this.settings.GentleVisuals; performance = this.settings.PerformanceMode;
        IsHitTestVisible = false;
        timer = new(TimeSpan.FromSeconds(1d / 30), DispatcherPriority.Background, (_, _) => Step());
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); attached = true; last = clock.Elapsed.TotalSeconds; dirty = true; timer.Start();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false; generation++; timer.Stop(); frame?.Dispose(); frame = null;
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) { dirty = true; generation++; }
    }
    private bool SurfaceActive() => attached && Active && IsVisible && TopLevel.GetTopLevel(this) is Window window
        && window.IsVisible && window.WindowState != WindowState.Minimized && (MotionAllowed?.Invoke() ?? window.IsActive);
    private void Step()
    {
        var now = clock.Elapsed.TotalSeconds; var dt = Math.Clamp(now - last, 0, .2); last = now;
        if (reduced != settings.ReducedMotion || gentle != settings.GentleVisuals || performance != settings.PerformanceMode)
        {
            if (!reduced && settings.ReducedMotion) phase = displayedPhase;
            dirty |= gentle != settings.GentleVisuals || performance != settings.PerformanceMode || frame is null;
            reduced = settings.ReducedMotion; gentle = settings.GentleVisuals; performance = settings.PerformanceMode;
            generation++;
        }
        timer.Interval = TimeSpan.FromSeconds(1d / (performance ? 15 : 30));
        if (!SurfaceActive()) return;
        if (!reduced) phase = (phase + dt * (gentle ? .25 : 1) * 1000 / LoomMath.LoopMilliseconds(definition.Speed)) % 1;
        if (pending is not null || reduced && !dirty || Bounds.Width < 1 || Bounds.Height < 1) return;
        dirty = false;
        var side = Math.Clamp((int)(Math.Max(Bounds.Width, Bounds.Height) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)), 64, performance ? 320 : 512);
        var q = definition; var position = phase; var version = generation; var soft = gentle; var cheap = performance;
        var work = Task.Run(() => LoomRenderer.Render(q, side, position, soft, cheap)); pending = work;
        _ = work.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(pending, work)) pending = null;
            if (!result.IsCompletedSuccessfully) { LastError = result.Exception?.GetBaseException().Message; return; }
            using var pixels = result.Result;
            if (!attached || version != generation || !SurfaceActive() || settings.ReducedMotion && !reduced
                || soft != settings.GentleVisuals || cheap != settings.PerformanceMode) return;
            if (frame?.PixelSize != new PixelSize(pixels.Width, pixels.Height))
            {
                frame?.Dispose(); frame = new(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using (var source = pixels.PeekPixels())
            using (var target = frame.Lock()) source.ReadPixels(pixels.Info, target.Address, target.RowBytes);
            displayedPhase = position; RenderedFrames++; LastError = null; InvalidateVisual();
        }), TaskScheduler.Default);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width / frame.Size.Width, Bounds.Height / frame.Size.Height);
        var width = frame.Size.Width * scale; var height = frame.Size.Height * scale;
        context.DrawImage(frame, new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height));
    }
}
