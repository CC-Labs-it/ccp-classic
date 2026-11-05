using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CCP.Classic.App.Effects;

// Capture exclusion is scoped to this surface, leaving ordinary effects visible in recordings.
internal sealed class BrainDrainWindow : Window
{
    private readonly Image picture = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private NativeOverlayInput? input;
    private Bitmap? frame;
    private bool released;
    internal PixelRect CaptureBounds { get; private set; }
    internal BrainDrainWindow(Action<Window> configureInput)
    {
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false;
        WindowDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.Manual;
        Background = TransparencyBackgroundFallback = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = picture; configureInput(this);
        Closed += (_, _) => Release();
    }
    internal bool Prepare(Screen screen, bool allowCapture)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            Position = new(-32000, -32000); Width = Height = 1; Show();
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent) return false;
            Hide(); input = NativeOverlayInput.Create(this, out _);
            if (input is null || !SetCapturePolicy(allowCapture)) return false;
            CaptureBounds = screen.Bounds; Position = screen.Bounds.Position;
            var scaling = DesktopScaling > 0 ? DesktopScaling : screen.Scaling;
            Width = screen.Bounds.Width / scaling; Height = screen.Bounds.Height / scaling;
            if (!input.SetBounds([], screen.Bounds.Size)) return false;
            Opacity = 0; Show();
            return ActualTransparencyLevel == WindowTransparencyLevel.Transparent && input.SetBounds([], PixelSize.FromSize(ClientSize, RenderScaling), force: true);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { return false; }
    }
    internal bool SetCapturePolicy(bool allowCapture)
    {
        var handle = TryGetPlatformHandle();
        return OperatingSystem.IsWindows() && handle is { HandleDescriptor: "HWND" } nativeHandle
            && SetWindowDisplayAffinity(nativeHandle.Handle, allowCapture ? 0u : 0x11u);
    }
    internal void Present(Bitmap value)
    {
        if (released) { value.Dispose(); return; }
        var previous = frame; frame = value; picture.Source = value; previous?.Dispose();
    }
    internal void Finish()
    {
        Release(); Close();
    }
    private void Release()
    {
        if (released) return; released = true; picture.Source = null; frame?.Dispose(); frame = null;
        input?.Dispose(); input = null;
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
