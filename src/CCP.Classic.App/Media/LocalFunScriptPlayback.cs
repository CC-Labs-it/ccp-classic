using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Media;

public readonly record struct ScriptOutput(double Intensity, double? Position);
internal sealed class LocalFunScriptPlayback : IDisposable
{
    private readonly Func<string, CancellationToken, Task<string?>> read;
    private CancellationTokenSource? cancellation;
    private Task<LocalFunScript?>? pending;
    private string? path;
    private double sourceTime = -1;
    private long reportAt;
    private bool disposed;
    public LocalFunScriptPlayback() : this(Read) { }
    internal LocalFunScriptPlayback(Func<string, CancellationToken, Task<string?>> reader) => read = reader;
    public void Clear()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; pending = null; path = null; sourceTime = -1;
    }
    public ScriptOutput Sample(IEnumerable<AudioPlaybackSample> videos, TimeProvider clock, bool enabled, bool convert)
    {
        if (disposed) return default;
        var source = enabled ? videos.Take(4).FirstOrDefault(value => value.IsPlaying && double.IsFinite(value.SourceSeconds) && value.SourceSeconds >= 0
            && clock.GetElapsedTime(value.ObservedAt).TotalMilliseconds is >= 0 and < 300) : default;
        if (source is null || LocalFunScript.CandidatePaths(source.Path).Length == 0) { Clear(); return default; }
        if (source.Path != path)
        {
            Clear(); path = source.Path; cancellation = new(); var token = cancellation.Token; var selected = source.Path;
            pending = Task.Run(async () =>
            {
                try
                {
                    foreach (var candidate in LocalFunScript.CandidatePaths(selected))
                    {
                        token.ThrowIfCancellationRequested(); var json = await read(candidate, token).ConfigureAwait(false);
                        if (json is not null) return LocalFunScript.TryParse(json, out var parsed) ? parsed : null;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException) { }
                return null;
            }, token);
        }
        if (sourceTime != source.SourceSeconds) { sourceTime = source.SourceSeconds; reportAt = clock.GetTimestamp(); }
        var age = clock.GetElapsedTime(reportAt).TotalMilliseconds;
        if (age is < 0 or > 900 || pending?.IsCompletedSuccessfully != true || pending.Result is not { } script) return default;
        var milliseconds = sourceTime * 1000 + age;
        if (milliseconds > script.DurationMilliseconds + 1000) return default;
        return new(convert ? script.IntensityAt(milliseconds) : 0, script.PositionAt(milliseconds + 300));
    }
    private static async Task<string?> Read(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > 10 * 1024 * 1024) return null;
        using var reader = new StreamReader(stream); return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }
    public void Dispose() { if (disposed) return; disposed = true; Clear(); }
}
