using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using CCP.Classic.App.Input;

namespace CCP.Classic.App.Effects;

// Simplified from CCP's Win32Overlay/X11Overlay. All calls and disposal stay on the owning UI thread.
internal sealed class NativeOverlayInput : IDisposable
{
    internal const int MaximumInputRegions = 16;
    private const int ExStyle = -20;
    private const uint Transparent = 0x20, NoActivate = 0x08000000, Layered = 0x80000, Tool = 0x80;
    private readonly Window window;
    private readonly IntPtr handle;
    private readonly bool win32;
    private readonly Win32Properties.CustomWindowStylesCallback? styleCallback;
    private IntPtr display;
    private IntPtr[] xWindows = [];
    private PixelRect[] lastBounds = [];
    private bool clickThrough = true, disposed, regionApplied;
    public string? LastError { get; private set; }

    private NativeOverlayInput(Window window, IntPtr handle, bool win32)
    {
        this.window = window; this.handle = handle; this.win32 = win32;
        if (win32)
        {
            styleCallback = (style, extended) => (style, WindowsStyle(extended, clickThrough));
            Win32Properties.AddWindowStylesCallback(window, styleCallback);
        }
    }

    internal static NativeOverlayInput? Create(Window window, out string? error)
    {
        error = null;
        var platform = window.TryGetPlatformHandle();
        if (platform is null || platform.Handle == IntPtr.Zero) { error = "The desktop window has no native handle."; return null; }
        var win = OperatingSystem.IsWindows() && platform.HandleDescriptor == "HWND";
        var x11 = OperatingSystem.IsLinux() && platform.HandleDescriptor == "XID" && IntPtr.Size == 8;
        if (!win && !x11) { error = "This desktop backend cannot provide input pass-through."; return null; }
        var input = new NativeOverlayInput(window, platform.Handle, win);
        try
        {
            if (win)
            {
                if (input.ApplyWindows(true) && SetWindowPos(input.handle, new IntPtr(-1), 0, 0, 0, 0, 0x13)) return input;
                error = "Windows refused overlay input pass-through.";
            }
            else if (input.OpenX11()) return input;
            else error = input.LastError;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { error = "Native desktop input support is unavailable: " + e.GetType().Name; }
        input.Dispose();
        return null;
    }

    internal bool SetBounds(IReadOnlyList<Rect> bounds, PixelSize size, bool force = false)
    {
        if (disposed) return false;
        var pixels = PixelBounds(bounds, size);
        if (win32)
        {
            var through = true;
            if (pixels.Length > 0)
            {
                if (!GetCursorPos(out var point) || !GetWindowRect(handle, out var area))
                    return Error("The desktop pointer position is unavailable.");
                through = !Hits(pixels, point.X - area.Left, point.Y - area.Top);
            }
            if (!force && through == clickThrough && ((GetWindowLong(handle, ExStyle) & Transparent) != 0) == through) return true;
            return ApplyWindows(through);
        }
        if (!force && regionApplied && lastBounds.AsSpan().SequenceEqual(pixels)) return true;
        foreach (var rect in pixels)
            if (rect.X > short.MaxValue || rect.Y > short.MaxValue || rect.Width > ushort.MaxValue || rect.Height > ushort.MaxValue)
                return Error("The monitor is too large for this desktop input region.");
        var rectangles = pixels.Select(rect => new XRectangle { X = (short)rect.X, Y = (short)rect.Y,
            Width = (ushort)rect.Width, Height = (ushort)rect.Height }).ToArray();
        var region = XFixesCreateRegion(display, rectangles.Length == 0 ? null : rectangles, rectangles.Length);
        if (region == IntPtr.Zero) return Error("The desktop input region could not be created.");
        try
        {
            foreach (var xid in xWindows) XFixesSetWindowShapeRegion(display, xid, 2, 0, 0, region);
            // Complete requests while the owning window is still alive. No process-wide error handler is installed.
            XSync(display, false);
        }
        finally { XFixesDestroyRegion(display, region); XSync(display, false); }
        lastBounds = pixels;
        regionApplied = true;
        return true;
    }

    private bool ApplyWindows(bool through)
    {
        clickThrough = through;
        var wanted = WindowsStyle(GetWindowLong(handle, ExStyle), through);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLong(handle, ExStyle, wanted);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) return Error("Windows refused the overlay input style.");
        if (!GetLayeredWindowAttributes(handle, out _, out _, out _) && !SetLayeredWindowAttributes(handle, 0, 255, 2))
            return Error("Windows refused overlay transparency.");
        return true;
    }

    private bool OpenX11()
    {
        var name = Environment.GetEnvironmentVariable("DISPLAY");
        if (!IsLocalDisplay(name)) return Error("Overlays require a local X11 display.");
        display = XOpenDisplay(name!);
        if (display == IntPtr.Zero) return Error("The local X11 display could not be opened.");
        if (XFixesQueryExtension(display, out _, out _) == 0) return Error("This X11 display has no input-region support.");
        var manager = XInternAtom(display, "_NET_WM_CM_S" + XDefaultScreen(display), false);
        if (XGetSelectionOwner(display, manager) == IntPtr.Zero) return Error("This X11 display has no transparency compositor.");
        var attributes = new XAttributes { OverrideRedirect = 1 };
        XChangeWindowAttributes(display, handle, 1u << 9, ref attributes);
        XSync(display, false);
        if (XQueryTree(display, handle, out _, out _, out var children, out var count) == 0)
            return Error("The X11 overlay hierarchy is unavailable.");
        try
        {
            if (count > 32) return Error("The X11 overlay hierarchy is unsupported.");
            xWindows = new IntPtr[count + 1]; xWindows[0] = handle;
            for (var i = 0; i < count; i++) xWindows[i + 1] = Marshal.ReadIntPtr(children, i * IntPtr.Size);
        }
        finally { if (children != IntPtr.Zero) XFree(children); }
        return true;
    }

    internal static uint WindowsStyle(uint extended, bool through) =>
        (extended | NoActivate | Layered | Tool) & ~Transparent | (through ? Transparent : 0);

    internal static bool IsLocalDisplay(string? value) => NativePanicKeys.IsLocalDisplay(value);

    internal static PixelRect[] PixelBounds(IReadOnlyList<Rect> bounds, PixelSize size)
    {
        var result = new List<PixelRect>(Math.Min(bounds.Count, MaximumInputRegions));
        foreach (var rect in bounds.Take(MaximumInputRegions))
        {
            if (!double.IsFinite(rect.X + rect.Y + rect.Width + rect.Height) || rect.Width <= 0 || rect.Height <= 0) continue;
            var clipped = rect.Intersect(new Rect(0, 0, 1, 1));
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;
            var left = (int)Math.Floor(clipped.X * size.Width); var top = (int)Math.Floor(clipped.Y * size.Height);
            var right = (int)Math.Ceiling(clipped.Right * size.Width); var bottom = (int)Math.Ceiling(clipped.Bottom * size.Height);
            if (right > left && bottom > top) result.Add(new PixelRect(left, top, right - left, bottom - top));
        }
        return result.ToArray();
    }

    internal static bool Hits(IEnumerable<PixelRect> rectangles, int x, int y) =>
        rectangles.Any(rect => x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom);

    private bool Error(string message) { LastError = message; return false; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (styleCallback is not null) Win32Properties.RemoveWindowStylesCallback(window, styleCallback);
        if (display != IntPtr.Zero) { XCloseDisplay(display); display = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct XRectangle { public short X, Y; public ushort Width, Height; }
    [StructLayout(LayoutKind.Explicit, Size = 112)] private struct XAttributes { [FieldOffset(88)] public int OverrideRedirect; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern uint SetWindowLong(IntPtr window, int index, uint value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointI point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectI rect);
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] private static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, nuint mask, ref XAttributes attributes);
    [DllImport("libX11.so.6")] private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libXfixes.so.3")] private static extern int XFixesQueryExtension(IntPtr display, out int events, out int errors);
    [DllImport("libXfixes.so.3")] private static extern IntPtr XFixesCreateRegion(IntPtr display, [In] XRectangle[]? rectangles, int count);
    [DllImport("libXfixes.so.3")] private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr region);
    [DllImport("libXfixes.so.3")] private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);
}
