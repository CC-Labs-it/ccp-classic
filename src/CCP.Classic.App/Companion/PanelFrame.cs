using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace CCP.Classic.App.Companion;

// The panel's visible frame in physical pixels. Avalonia's Position and FrameSize on Windows 10/11
// include the invisible resize borders (about 7 px a side), which left daylight between the tube and
// the panel; DWM's extended frame bounds are what the player sees. X11 has no invisible borders.
internal static class PanelFrame
{
    private const int ExtendedFrameBounds = 9;

    public static PixelRect Visible(Window window, double scaling)
    {
        var outer = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize ?? window.ClientSize, scaling));
        return TubeLayout.VisibleFrame(outer, OperatingSystem.IsWindows() ? Extended(window) : null);
    }

    private static PixelRect? Extended(Window window)
    {
        var platform = window.TryGetPlatformHandle();
        if (platform is null || platform.Handle == IntPtr.Zero || platform.HandleDescriptor != "HWND") return null;
        try
        {
            return DwmGetWindowAttribute(platform.Handle, ExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) == 0
                ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
}
