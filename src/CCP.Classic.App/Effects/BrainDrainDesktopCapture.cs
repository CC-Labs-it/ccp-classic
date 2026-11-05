using System.Runtime.InteropServices;
using Avalonia;

namespace CCP.Classic.App.Effects;

internal interface IBrainDrainDesktopCapture : IDisposable
{
    void SetScreens(IReadOnlyList<PixelRect> screens);
    BrainDrainCapture? Capture(PixelRect screen, bool performance);
}

// Public CCP's persistent DIB and plain SRCCOPY capture path. Called only by the capture worker.
// Layered overlays are omitted by SRCCOPY; do not add CAPTUREBLT, which causes self-feedback.
internal sealed class BrainDrainDesktopCapture : IBrainDrainDesktopCapture
{
    private readonly Dictionary<PixelRect, Slot> slots = [];
    public void SetScreens(IReadOnlyList<PixelRect> screens)
    {
        foreach (var key in slots.Keys.Where(key => !screens.Contains(key)).ToArray())
        { slots[key].Dispose(); slots.Remove(key); }
    }
    public BrainDrainCapture? Capture(PixelRect screen, bool performance)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Safe desktop capture is unavailable");
        if (screen.Width is <= 0 or > 32768 || screen.Height is <= 0 or > 32768) return null;
        var downscale = Math.Max(performance ? 8 : 4, Math.Max((int)Math.Ceiling(screen.Width / 960d), (int)Math.Ceiling(screen.Height / 540d)));
        if (slots.TryGetValue(screen, out var existing) && existing.Downscale != downscale)
        { existing.Dispose(); slots.Remove(screen); }
        if (!slots.TryGetValue(screen, out var slot))
        {
            if (slots.Count >= 8) return null;
            slot = new Slot(screen, downscale); slots.Add(screen, slot);
        }
        var source = GetDC(0);
        if (source == 0) return null;
        try
        {
            _ = SetStretchBltMode(slot.Dc, 4);
            if (!StretchBlt(slot.Dc, 0, 0, slot.Width, slot.Height, source, screen.X, screen.Y,
                screen.Width, screen.Height, 0x00CC0020)) return null;
            _ = GdiFlush();
            return new(slot.Pixels, slot.Width, slot.Height, slot.Width * 4, downscale);
        }
        finally { _ = ReleaseDC(0, source); }
    }
    public void Dispose() { foreach (var slot in slots.Values) slot.Dispose(); slots.Clear(); }

    private sealed class Slot : IDisposable
    {
        public readonly int Width, Height, Downscale;
        public readonly nint Dc, Pixels;
        private readonly nint bitmap, previous;
        public Slot(PixelRect screen, int downscale)
        {
            Downscale = downscale; Width = Math.Max(2, screen.Width / downscale & ~1); Height = Math.Max(2, screen.Height / downscale & ~1);
            Dc = CreateCompatibleDC(0);
            if (Dc == 0) throw new IOException("Desktop capture surface is unavailable");
            var info = new BitmapInfo { Size = Marshal.SizeOf<BitmapInfo>(), Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(Dc, ref info, 0, out Pixels, 0, 0);
            if (bitmap == 0 || Pixels == 0)
            { if (bitmap != 0) _ = DeleteObject(bitmap); _ = DeleteDC(Dc); throw new IOException("Desktop capture bitmap is unavailable"); }
            previous = SelectObject(Dc, bitmap);
            if (previous == 0 || previous == -1)
            { _ = DeleteObject(bitmap); _ = DeleteDC(Dc); throw new IOException("Desktop capture bitmap cannot be selected"); }
        }
        public void Dispose() { _ = SelectObject(Dc, previous); _ = DeleteObject(bitmap); _ = DeleteDC(Dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, ImageBytes, XResolution, YResolution, Colours, ImportantColours;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint destination, int x, int y, int width, int height, nint source,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);
}
