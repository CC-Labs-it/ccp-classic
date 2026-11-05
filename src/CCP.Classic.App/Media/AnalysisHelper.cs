using System.Security.Cryptography;
using System.Text.Json;

namespace CCP.Classic.App.Media;

internal static class AnalysisHelper
{
    internal static string Resolve(CancellationToken token)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Analysis");
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        if (OperatingSystem.IsWindows() || File.Exists(Path.Combine(directory, name)))
            return VerifyBundle(directory, OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", name, token);
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/ffmpeg")) return "/usr/bin/ffmpeg";
        throw new IOException("Offline audio analysis helper is unavailable.");
    }
    internal static string VerifyBundle(string directory, string runtime, string executable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var manifest = Path.Combine(directory, "analysis-helper.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 32768) throw new IOException("Audio analysis helper manifest is unavailable.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest)); var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("runtime").GetString() != runtime)
            throw new IOException("Audio analysis helper manifest is incompatible.");
        var files = root.GetProperty("files").EnumerateArray().ToArray(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (files.Length is < 1 or > 32) throw new IOException("Audio analysis helper manifest exceeds its budget.");
        var buffer = new byte[65536];
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var name = file.GetProperty("name").GetString(); var expected = file.GetProperty("sha256").GetString();
            if (name is null || name.Length > 128 || name != Path.GetFileName(name) || name.Contains(':') || name.Contains('\\') || name.Contains('/')
                || (name != executable && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) || !names.Add(name)
                || expected is null || expected.Length != 64 || !expected.All(char.IsAsciiHexDigit))
                throw new IOException("Audio analysis helper manifest is invalid.");
            var path = Path.Combine(directory, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Audio analysis helper links are unsupported.");
            using var stream = File.OpenRead(path);
            if (stream.Length != file.GetProperty("size").GetInt64() || stream.Length > 256L * 1024 * 1024)
                throw new IOException("Audio analysis helper size does not match.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                token.ThrowIfCancellationRequested(); var read = stream.Read(buffer);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Audio analysis helper integrity check failed.");
        }
        if (!names.Contains(executable)) throw new IOException("Audio analysis executable is missing.");
        return Path.Combine(directory, executable);
    }
}
