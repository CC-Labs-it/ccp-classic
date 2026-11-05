using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Effects;

// One bounded worker supplies a shared frame for every selected monitor.
internal sealed class LiveLoom(ClassicSettings settings, LoomStore store) : IDisposable
{
    private string? selected;
    private LoomDefinition? definition;
    private WriteableBitmap? image;
    private Task? pending;
    private int generation, selectionGeneration;
    private bool disposed, reduced, gentle, performance, dirty, loadAttempted;
    private double last, phase;
    public WriteableBitmap? Image => image;
    internal double Phase => phase;
    internal bool IsBusy => pending is not null;

    public void Update(double elapsed)
    {
        if (disposed) return;
        var dt = Math.Clamp(elapsed - last, 0, .2); last = elapsed;
        if (selected != settings.CustomSpiralId)
        {
            Clear(); selected = settings.CustomSpiralId;
        }
        if (selected is not null && !loadAttempted && pending is null) Load(selected);
        if (reduced != settings.ReducedMotion || gentle != settings.GentleVisuals || performance != settings.PerformanceMode)
        {
            reduced = settings.ReducedMotion; gentle = settings.GentleVisuals; performance = settings.PerformanceMode;
            generation++; dirty = true;
        }
        if (definition is null || !settings.Effects["spiral"].Enabled) return;
        if (!reduced) phase = (phase + dt * (gentle ? .25 : 1) * 1000 / LoomMath.LoopMilliseconds(definition.Speed)) % 1;
        if (pending is not null || reduced && !dirty && image is not null) return;
        Render();
    }

    private void Load(string id)
    {
        var version = selectionGeneration; loadAttempted = true;
        var work = Task.Run(() => store.LoadAll().FirstOrDefault(item => item.Id == id)?.Definition);
        pending = work;
        _ = work.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(pending, work)) pending = null;
            if (!disposed && version == selectionGeneration && selected == id && result.IsCompletedSuccessfully)
            { definition = result.Result; dirty = true; }
        }), TaskScheduler.Default);
    }

    private void Render()
    {
        var q = definition!; var position = phase; var version = generation; var soft = gentle; var cheap = performance;
        dirty = false;
        var work = Task.Run(() => LoomRenderer.Render(q, cheap ? 320 : 512, position, soft, cheap));
        pending = work;
        _ = work.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(pending, work)) pending = null;
            if (!result.IsCompletedSuccessfully) { dirty = true; return; }
            using var pixels = result.Result;
            if (disposed || version != generation || settings.CustomSpiralId != selected
                || soft != settings.GentleVisuals || cheap != settings.PerformanceMode) return;
            if (image?.PixelSize != new PixelSize(pixels.Width, pixels.Height))
            {
                image?.Dispose();
                image = new(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using var source = pixels.PeekPixels(); using var target = image.Lock();
            source.ReadPixels(pixels.Info, target.Address, target.RowBytes);
        }), TaskScheduler.Default);
    }

    public void Reload() { Clear(); selected = null; }
    public void Reset() { Clear(); selected = null; last = phase = 0; }
    private void Clear()
    {
        generation++; selectionGeneration++; loadAttempted = false; definition = null; image?.Dispose(); image = null; dirty = true;
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); }
}
