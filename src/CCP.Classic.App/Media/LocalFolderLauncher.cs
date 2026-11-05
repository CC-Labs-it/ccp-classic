using System.Diagnostics;
using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

internal static class LocalFolderLauncher
{
    internal static bool Open(string path, Action<string>? launch = null)
    {
        if (!IsVerified(path)) return false;
        (launch ?? (folder => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })))(Path.GetFullPath(path));
        return true;
    }
    internal static bool IsVerified(string? path)
    {
        if (!MediaLibrary.IsLocalPath(path)) return false;
        try
        {
            var full = Path.GetFullPath(path!);
            if (!Directory.Exists(full)) return false;
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) return false;
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
}
