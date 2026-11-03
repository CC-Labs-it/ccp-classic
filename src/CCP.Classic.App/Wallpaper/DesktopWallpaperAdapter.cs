using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace CCP.Classic.App.Wallpaper;

public readonly record struct WallpaperMonitor(PixelRect Bounds, double Scaling);
public readonly record struct WallpaperDesktopState(bool ParentAvailable, bool Covered = false, bool Minimized = false, bool OnBattery = false);
public interface IDesktopWallpaperAdapter
{
    bool IsSupported { get; }
    bool Attach(Window window, WallpaperMonitor monitor);
    WallpaperDesktopState ReadState(Window window, WallpaperMonitor monitor);
    void Detach(Window window);
}

// Desktop attachment and visibility rules adapted from public CCP DynamicWallpaperService.Desktop, MIT.
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopWallpaperAdapter : IDesktopWallpaperAdapter
{
    private readonly Dictionary<Window, (nint Handle, nint Parent, Win32Properties.CustomWindowStylesCallback Styles)> attached = [];
    public bool IsSupported => true;
    public bool Attach(Window window, WallpaperMonitor monitor)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "HWND" || handle.Handle == 0 || window.ActualTransparencyLevel != WindowTransparencyLevel.Transparent) return false;
        var progman = FindWindow("Progman", null); if (progman == 0) return false;
        SendMessageTimeout(progman, 0x052c, new nint(0xd), new nint(1), 0, 200, out _);
        nint worker = 0, icons = 0;
        EnumWindows((candidate, _) =>
        {
            var found = FindWindowEx(candidate, 0, "SHELLDLL_DefView", null);
            if (found != 0) { icons = found; worker = FindWindowEx(0, candidate, "WorkerW", null); }
            return true;
        }, 0);
        var raised = (GetWindowLong(progman, -20) & 0x00200000) != 0;
        var parent = raised ? progman : worker != 0 ? worker : FindWindowEx(progman, 0, "WorkerW", null);
        if (raised) icons = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (parent == 0 || raised && icons == 0) return false;
        Win32Properties.CustomWindowStylesCallback styles = ChildStyles;
        Win32Properties.AddWindowStylesCallback(window, styles);
        var successful = false;
        try
        {
            var current = ChildStyles(unchecked((uint)GetWindowLong(handle.Handle, -16)), unchecked((uint)GetWindowLong(handle.Handle, -20)));
            SetWindowLong(handle.Handle, -16, unchecked((int)current.Item1)); SetWindowLong(handle.Handle, -20, unchecked((int)current.Item2));
            if (!SetLayeredWindowAttributes(handle.Handle, 0, 255, 2)) return false;
            Marshal.SetLastPInvokeError(0);
            var previous = SetParent(handle.Handle, parent);
            if (previous == 0 && Marshal.GetLastPInvokeError() != 0) return false;
            window.Width = monitor.Bounds.Width / monitor.Scaling; window.Height = monitor.Bounds.Height / monitor.Scaling;
            var origin = new NativePoint { X = monitor.Bounds.X, Y = monitor.Bounds.Y }; MapWindowPoints(0, parent, ref origin, 1);
            successful = SetWindowPos(handle.Handle, raised ? icons : 0, origin.X, origin.Y, monitor.Bounds.Width, monitor.Bounds.Height, 0x10 | 0x40);
            if (successful) attached.Add(window, (handle.Handle, parent, styles));
            return successful;
        }
        finally { if (!successful) Win32Properties.RemoveWindowStylesCallback(window, styles); }
    }
    internal static (uint, uint) ChildStyles(uint style, uint extended) =>
        ((style & ~0x80000000u) | 0x40000000u, (extended | 0x080800a0u) & ~0x00040008u);

    public WallpaperDesktopState ReadState(Window window, WallpaperMonitor monitor)
    {
        if (!attached.TryGetValue(window, out var target) || !IsWindow(target.Handle) || !IsWindow(target.Parent)
            || window.ActualTransparencyLevel != WindowTransparencyLevel.Transparent
            || (unchecked((uint)GetWindowLong(target.Handle, -20)) & 0x080800a0u) != 0x080800a0u) return new(false);
        var battery = !GetSystemPowerStatus(out var power) || power.AcLineStatus == 0;
        var minimized = IsIconic(target.Handle) || !IsWindowVisible(target.Handle);
        var foreground = GetForegroundWindow(); var name = new StringBuilder(64); GetClassName(foreground, name, name.Capacity);
        var covered = false;
        if (foreground != 0 && !IsIconic(foreground) && name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            && GetWindowRect(foreground, out var rect))
        {
            var centre = new NativePoint { X = monitor.Bounds.X + monitor.Bounds.Width / 2, Y = monitor.Bounds.Y + monitor.Bounds.Height / 2 };
            covered = IsZoomed(foreground) && MonitorFromWindow(foreground, 2) == MonitorFromPoint(centre, 2)
                || rect.Left <= monitor.Bounds.X && rect.Top <= monitor.Bounds.Y && rect.Right >= monitor.Bounds.Right && rect.Bottom >= monitor.Bounds.Bottom;
        }
        return new(true, covered, minimized, battery);
    }
    public void Detach(Window window)
    {
        if (!attached.Remove(window, out var target)) return;
        Win32Properties.RemoveWindowStylesCallback(window, target.Styles);
        if (IsWindow(target.Parent)) RedrawWindow(target.Parent, 0, 0, 0x1 | 0x4 | 0x80);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, Reserved; public uint BatteryLifeTime, BatteryFullLifeTime; }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string name, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, ref NativePoint point, uint count);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint window, nint update, nint region, uint flags);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}

internal sealed class UnsupportedDesktopWallpaperAdapter : IDesktopWallpaperAdapter
{
    public bool IsSupported => false;
    public bool Attach(Window window, WallpaperMonitor monitor) => false;
    public WallpaperDesktopState ReadState(Window window, WallpaperMonitor monitor) => new(false);
    public void Detach(Window window) { }
}
