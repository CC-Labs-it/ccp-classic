using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace CCP.Classic.Core;

public readonly record struct StartupRegistrationResult(bool Succeeded, string? Error = null);

// Construction never changes startup. Only an explicit user preference calls SetEnabled.
public sealed class StartupRegistration
{
    public const string RunValueName = "CCP Classic";
    public const string DesktopFileName = "ccp-classic.desktop";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string configurationDirectory;
    private readonly bool windows, supported;
    private readonly Action<string?>? windowsWriter;

    public StartupRegistration(string? configurationDirectory = null, bool? windows = null, Action<string?>? windowsWriter = null)
    {
        this.configurationDirectory = configurationDirectory ?? DefaultConfigurationDirectory();
        this.windows = windows ?? OperatingSystem.IsWindows();
        supported = windows.HasValue || OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        this.windowsWriter = windowsWriter;
    }

    public StartupRegistrationResult SetEnabled(bool enabled, string? executable = null, string? applicationDll = null)
    {
        try
        {
            if (!supported) return new(false, "Startup is unavailable on this platform.");
            if (windows)
            {
                var command = enabled ? BuildWindowsCommand(executable!, applicationDll) : null;
                if (windowsWriter is not null) windowsWriter(command);
                else if (OperatingSystem.IsWindows()) WriteWindows(command);
                else return new(false, "Windows startup is unavailable on this platform.");
            }
            else
            {
                var entry = enabled ? BuildDesktopEntry(executable!, applicationDll) : null;
                if (!Path.IsPathFullyQualified(configurationDirectory) || InvalidCharacters(configurationDirectory)
                    || configurationDirectory.StartsWith("//", StringComparison.Ordinal) || configurationDirectory.StartsWith(@"\\", StringComparison.Ordinal))
                    throw new ArgumentException("Startup configuration directory must be local and absolute.");
                var directory = Path.Combine(configurationDirectory, "autostart");
                var target = Path.Combine(directory, DesktopFileName);
                if (entry is null)
                {
                    try { File.Delete(target); } catch (DirectoryNotFoundException) { }
                }
                else
                {
                    Directory.CreateDirectory(directory);
                    var temporary = Path.Combine(directory, ".ccp-classic-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try { File.WriteAllText(temporary, entry, new UTF8Encoding(false)); File.Move(temporary, target, overwrite: true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            return new(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or PlatformNotSupportedException or System.Security.SecurityException)
        { return new(false, error.Message); }
    }

    public static string BuildWindowsCommand(string executable, string? applicationDll = null)
    {
        ValidatePath(executable, windows: true, executable: true);
        if (applicationDll is not null) ValidatePath(applicationDll, windows: true, executable: false);
        var command = '"' + executable + '"' + (applicationDll is null ? "" : " \"" + applicationDll + '"') + " --startup";
        if (command.Length > 260) throw new ArgumentException("Windows startup command exceeds 260 characters.");
        return command;
    }

    public static string BuildDesktopEntry(string executable, string? applicationDll = null)
    {
        ValidatePath(executable, windows: false, executable: true);
        if (applicationDll is not null) ValidatePath(applicationDll, windows: false, executable: false);
        var command = DesktopQuote(executable) + (applicationDll is null ? "" : " " + DesktopQuote(applicationDll)) + " --startup";
        return "[Desktop Entry]\nType=Application\nName=CCP Classic\nExec=" + command
            + "\nTerminal=false\nStartupNotify=false\nX-GNOME-Autostart-enabled=true\n";
    }

    private static string DesktopQuote(string value)
    {
        // Desktop string unescaping happens before Exec argument unquoting.
        var quoted = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
        return '"' + quoted.Replace("\\", "\\\\", StringComparison.Ordinal) + '"';
    }
    private static bool InvalidCharacters(string value) => value.Any(character => char.IsControl(character) || character == '`');
    private static void ValidatePath(string value, bool windows, bool executable)
    {
        if (string.IsNullOrWhiteSpace(value) || InvalidCharacters(value) || value.Contains("://", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Startup requires a fully qualified local file path.");
        if (windows)
        {
            if (value.Length < 4 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] is not ('/' or '\\')
                || value.AsSpan(2).Contains(':') || value.Any(character => character is '"' or '<' or '>' or '|' or '?' or '*'))
                throw new ArgumentException("Windows startup path is invalid.");
            if (executable && !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Startup executable must be an .exe file.");
        }
        else if (!value.StartsWith('/') || executable && value.Contains('=')) throw new ArgumentException("Linux startup path is invalid.");
        if (!executable && !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Startup application must be a .dll file.");
    }

    private static string DefaultConfigurationDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return configured is { Length: > 0 } && Path.IsPathFullyQualified(configured) ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }
    [SupportedOSPlatform("windows")]
    private static void WriteWindows(string? command)
    {
        if (command is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            existing?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new IOException("Windows startup key is unavailable.");
            key.SetValue(RunValueName, command, RegistryValueKind.String);
        }
    }
}
