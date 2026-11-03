using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

internal sealed class CornerGifWindow : Window
{
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private NativeOverlayInput? input;
    private bool prepared;
    internal CornerGifWindow(Action<Window> configureInput)
    {
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false;
        WindowDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.Manual;
        Background = TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Content = image; configureInput(this);
        Closed += (_, _) => { prepared = false; image.Source = null; input?.Dispose(); input = null; };
    }
    internal bool Update(Screen screen, CornerGifOptions options, IImage source, int stacked, double alpha)
    {
        try
        {
            if (!prepared)
            {
                Position = new(-32000, -32000); Width = Height = 1; Show();
                if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent) return false;
                Hide(); input = NativeOverlayInput.Create(this, out _);
                if (input is null) return false;
                prepared = true;
            }
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var box = BoundsFor(new(screen.Bounds.Width / scaling, screen.Bounds.Height / scaling), source.Size, options, stacked);
            if (box.Width <= 0 || box.Height <= 0) return false;
            image.Source = source; Width = box.Width; Height = box.Height;
            Position = screen.Bounds.Position + new PixelPoint((int)Math.Round(box.X * scaling), (int)Math.Round(box.Y * scaling));
            Opacity = Math.Clamp(alpha, 0, 1);
            if (!input!.SetBounds([], PixelSize.FromSize(box.Size, scaling))) return false;
            if (!IsVisible) Show();
            image.InvalidateVisual(); return ActualTransparencyLevel == WindowTransparencyLevel.Transparent;
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { return false; }
    }
    internal static Rect BoundsFor(Size screen, Size source, CornerGifOptions options, int stacked)
    {
        if (screen.Width <= 0 || screen.Height <= 0 || source.Width <= 0 || source.Height <= 0) return default;
        var size = Math.Min(Math.Clamp(options.Size, 100, 800), Math.Min(screen.Width, screen.Height));
        var width = source.Width / Math.Max(source.Width, source.Height) * size;
        var height = source.Height / Math.Max(source.Width, source.Height) * size;
        var right = options.Position is SpiralCorner.TopRight or SpiralCorner.BottomRight;
        var bottom = options.Position is SpiralCorner.BottomLeft or SpiralCorner.BottomRight;
        var inset = Math.Clamp(stacked, 0, 1) * 40;
        return new(Math.Clamp(right ? screen.Width - width - inset : inset, 0, screen.Width - width),
            Math.Clamp(bottom ? screen.Height - height - inset : inset, 0, screen.Height - height), width, height);
    }
    internal void Finish() { image.Source = null; Close(); }
}
