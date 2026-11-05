using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CCP.Classic.App.Wallpaper;

public sealed record WallpaperSnapshot(string Path, bool SolidColor, string Style, string Tile, bool CanReplace);
public interface IWallpaperNative
{
    bool IsSupported { get; }
    WallpaperSnapshot Capture();
    bool Apply(string imagePath);
    bool Restore(WallpaperSnapshot snapshot, string imagePath);
}

// MIT public CCP WallpaperService/WallpaperService.Collage: never freeze a slideshow or per-monitor setup.
[SupportedOSPlatform("windows")]
public sealed class WindowsWallpaperNative : IWallpaperNative
{
    public bool IsSupported => true;
    public WallpaperSnapshot Capture()
    {
        using var background = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var type = background?.GetValue("BackgroundType") is int value ? value : -1;
        if (desktop is null || type is < 0 or > 1) return new("", false, "", "", false);
        var paths = new List<string>();
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            var count = wallpaper.GetMonitorDevicePathCount();
            if (count is 0 or > 64) return new("", false, "", "", false);
            for (uint index = 0; index < count; index++) paths.Add(wallpaper.GetWallpaper(wallpaper.GetMonitorDevicePathAt(index)));
        }
        finally { Marshal.ReleaseComObject(wallpaper); }
        var same = paths.All(path => string.Equals(path, paths[0], StringComparison.OrdinalIgnoreCase));
        return new(type == 1 ? "" : paths[0], type == 1, desktop.GetValue("WallpaperStyle")?.ToString() ?? "0",
            desktop.GetValue("TileWallpaper")?.ToString() ?? "0", same);
    }
    public bool Apply(string imagePath)
    {
        if (SystemParametersInfo(0x14, 0, imagePath, 1) == 0) return false;
        // A synchronous SENDCHANGE broadcast can wait on every desktop application.
        SendNotifyMessage(new nint(0xffff), 0x1a, new nint(0x14), 0); return true;
    }
    public bool Restore(WallpaperSnapshot snapshot, string imagePath)
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        if (desktop is null) return false;
        desktop.SetValue("WallpaperStyle", snapshot.Style, RegistryValueKind.String);
        desktop.SetValue("TileWallpaper", snapshot.Tile, RegistryValueKind.String);
        return Apply(snapshot.SolidColor ? "" : imagePath);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(uint action, uint parameter, string value, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(nint window, uint message, nint wParam, nint lParam);
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
    }
    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass;
}

internal sealed class UnsupportedWallpaperNative : IWallpaperNative
{
    public bool IsSupported => false;
    public WallpaperSnapshot Capture() => throw new PlatformNotSupportedException();
    public bool Apply(string path) => false;
    public bool Restore(WallpaperSnapshot snapshot, string path) => false;
}
