using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CCP.Classic.App.Media;

internal sealed class FfmpegEnvelopeDecoder(string? testExecutable = null, Action<Process>? started = null, Action<Exception>? failed = null)
{
    internal const long MaximumFrames = 7200L * 48000;
    internal void Decode(string path, PcmRmsAccumulator accumulator, CancellationToken token) =>
        Run(path, accumulator, token).GetAwaiter().GetResult();

    private async Task Run(string path, PcmRmsAccumulator accumulator, CancellationToken token)
    {
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); stop.CancelAfter(TimeSpan.FromSeconds(60));
            var active = stop.Token;
            active.ThrowIfCancellationRequested(); AnalysisProcessBudget.RequireHeadroom();
            var local = LocalAudioInput.LocalPath(path) ?? LocalVideoPlayback.LocalPath(path)
                ?? throw new IOException("A supported local media file is required.");
            using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length > 8L * 1024 * 1024 * 1024) throw new IOException("Media exceeds the audio analysis budget.");
            var header = new byte[12]; active.ThrowIfCancellationRequested(); var count = source.Read(header); source.Position = 0;
            var demux = Demux(header.AsSpan(0, count), Path.GetExtension(local))
                ?? throw new IOException("Unsupported audio analysis container.");
            var executable = testExecutable ?? AnalysisHelper.Resolve(active);
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new IOException("Offline audio analysis helper is unavailable.");
            // Linux opens the already-validated inode through our held descriptor. It stays seekable.
            var input = OperatingSystem.IsLinux() ? $"/proc/{Environment.ProcessId}/fd/{source.SafeFileHandle.DangerousGetHandle()}" : local;
            using var child = new Process { StartInfo = StartInfo(executable, input, demux) };
            using var budget = new AnalysisProcessBudget();
            try
            {
                active.ThrowIfCancellationRequested(); AnalysisProcessBudget.RequireHeadroom();
                if (!child.Start()) throw new IOException("Audio analysis did not start.");
                budget.Attach(child); started?.Invoke(child);
                using var cancellation = stop.Token.Register(budget.Stop);
                var pcm = Guard(() => ReadPcm(child.StandardOutput.BaseStream, accumulator, stop.Token));
                var errors = Guard(() => Drain(child.StandardError.BaseStream, stop.Token));
                var monitor = Guard(async () =>
                {
                    while (!child.HasExited) { stop.Token.ThrowIfCancellationRequested(); budget.Check(); await Task.Delay(50, stop.Token).ConfigureAwait(false); }
                });
                await Task.WhenAll(pcm, errors, monitor, child.WaitForExitAsync(stop.Token)).ConfigureAwait(false);
                if (child.ExitCode != 0) throw new IOException("Audio analysis could not decode this media.");
                async Task Guard(Func<Task> action)
                {
                    try { await action().ConfigureAwait(false); }
                    catch { budget.Stop(); stop.Cancel(); throw; }
                }
            }
            finally
            {
                budget.Stop();
                try { if (!child.HasExited) child.WaitForExit(1000); }
                catch (InvalidOperationException) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        { failed?.Invoke(failure); throw new IOException("Offline audio analysis is unavailable."); }
    }

    internal static string? Demux(ReadOnlySpan<byte> header, string extension)
    {
        var detected = LocalAudioInput.DetectDemux(header, extension) ?? LocalVideoPlayback.Demux(header);
        return detected switch { "mp4" => "mov", "mkv" => "matroska", "wav" or "mp3" or "ogg" or "flac" or "aac" or "avi" => detected, _ => null };
    }
    internal static ProcessStartInfo StartInfo(string executable, string path, string demux)
    {
        if (demux is not ("wav" or "mp3" or "ogg" or "flac" or "aac" or "avi" or "mov" or "matroska")) throw new ArgumentException("Unsupported demuxer.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "-nostdin", "-v", "error", "-threads", "2", "-filter_threads", "1", "-filter_complex_threads", "1",
            "-max_alloc", "67108864", "-protocol_whitelist", "file,pipe", "-copyts", "-f", demux, "-i", path,
            "-map", "0:a:0", "-vn", "-sn", "-dn", "-af", "aresample=48000:async=1:first_pts=0",
            "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-threads", "2", "-t", "7200", "-f", "s16le", "pipe:1" }) info.ArgumentList.Add(argument);
        return info;
    }
    internal static async Task ReadPcm(Stream stream, PcmRmsAccumulator accumulator, CancellationToken token, long maximumFrames = MaximumFrames)
    {
        var buffer = new byte[65536 + 3]; var carry = 0; long frames = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(carry, buffer.Length - carry), token).ConfigureAwait(false);
            if (read == 0) { if (carry != 0) throw new IOException("Incomplete PCM frame."); return; }
            var bytes = read + carry; var complete = bytes & ~3;
            if (frames + complete / 4 > maximumFrames) throw new IOException("Decoded audio exceeds its duration budget.");
            if (complete > 0)
            {
                token.ThrowIfCancellationRequested();
                if (!BitConverter.IsLittleEndian) for (var offset = 0; offset < complete; offset += 2) (buffer[offset], buffer[offset + 1]) = (buffer[offset + 1], buffer[offset]);
                accumulator.Append(MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, complete)), frames / 48000d);
                frames += complete / 4;
            }
            carry = bytes - complete; buffer.AsSpan(complete, carry).CopyTo(buffer);
        }
    }
    private static async Task Drain(Stream stream, CancellationToken token)
    {
        var buffer = new byte[1024];
        while (true) { token.ThrowIfCancellationRequested(); if (await stream.ReadAsync(buffer, token).ConfigureAwait(false) == 0) return; }
    }
}
