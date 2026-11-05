using System.Runtime.InteropServices;
using System.Diagnostics;
using LibVLCSharp.Shared;

namespace CCP.Classic.App.Media;

internal sealed class NativeAudioDevice : IAudioDevice
{
    private static readonly object InitializationGate = new();
    private static bool initialized, resolverInstalled;
    private readonly LibVLC vlc;

    public NativeAudioDevice()
    {
        EnsureInitialized();
        vlc = new LibVLC(Options(OperatingSystem.IsWindows()));
    }

    internal static void EnsureInitialized()
    {
        lock (InitializationGate)
        {
            if (!initialized)
            {
                if (OperatingSystem.IsLinux() && !resolverInstalled)
                {
                    NativeLibrary.SetDllImportResolver(typeof(LibVLCSharp.Shared.Core).Assembly,
                        (name, _, _) => Resolve(name, candidate => NativeLibrary.TryLoad(candidate, out var handle) ? handle : IntPtr.Zero));
                    resolverInstalled = true;
                }
                var bundled = new[] { "", Environment.Is64BitProcess ? "win-x64" : "win-x86" }
                    .Select(sub => Path.Combine(AppContext.BaseDirectory, "libvlc", sub))
                    .FirstOrDefault(folder => File.Exists(Path.Combine(folder, "libvlc.dll")));
                LibVLCSharp.Shared.Core.Initialize(OperatingSystem.IsWindows() ? bundled : null);
                initialized = true;
            }
        }
    }

    internal static string[] Options(bool windows)
    {
        var options = new List<string> { "--quiet", "--no-video", "--no-media-library", "--no-lua", "--no-metadata-network-access" };
        // Never fall back to an output that changes the whole process's mixer session.
        if (windows) options.Add("--aout=directsound");
        return options.ToArray();
    }

    internal static IntPtr Resolve(string name, Func<string, IntPtr> load)
    {
        var candidates = name switch
        {
            "libvlc" => new[] { "libvlc.so", "libvlc.so.5" },
            "libvlccore" => new[] { "libvlccore.so", "libvlccore.so.9" },
            _ => Array.Empty<string>()
        };
        foreach (var candidate in candidates)
        {
            var handle = load(candidate);
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    internal static int PlayerVolume(double gain) => (int)Math.Round(Math.Cbrt(Math.Clamp(gain, 0, 1)) * 100);
    public IAudioVoice Open(string path, double gain, Action wake) => new Voice(vlc, path, gain, wake);
    public void Dispose() => vlc.Dispose();

    private sealed class Voice : IAudioVoice
    {
        private LocalAudioInput? local;
        private StreamMediaInput? input;
        private LibVLCSharp.Shared.Media? media;
        private MediaPlayer? player;
        private readonly VideoPlaybackAllowance allowance = new();
        private readonly Stopwatch elapsed = new();
        private int volume;
        private int finished, applyGain, failed, successful;
        private long length;
        private long lastPosition = -1, lastProgressAt;
        private long sourceMilliseconds;
        private int isPlaying;
        private string? outputId;
        public bool IsFinished => Volatile.Read(ref finished) != 0;
        public bool WasSuccessful => Volatile.Read(ref successful) != 0 && Volatile.Read(ref failed) == 0;
        public double PlayedSeconds => Math.Max(allowance.PlayedMilliseconds, WasSuccessful ? Interlocked.Read(ref length) : 0) / 1000d;
        public double SourceSeconds => Interlocked.Read(ref sourceMilliseconds) / 1000d;
        public bool IsPlaying => Volatile.Read(ref isPlaying) != 0 && !IsFinished;

        public Voice(LibVLC vlc, string path, double gain, Action wake)
        {
            try
            {
                local = new LocalAudioInput(path);
                input = new StreamMediaInput(local.Stream);
                media = new LibVLCSharp.Shared.Media(vlc, input);
                media.AddOption(":no-video");
                media.AddOption(":demux=" + local.Demux + ",none");
                media.AddOption(":no-metadata-network-access");
                media.SubItemAdded += (_, _) => { Interlocked.Exchange(ref failed, 1); Interlocked.Exchange(ref finished, 1); wake(); };
                player = new MediaPlayer(media);
                volume = PlayerVolume(gain);
                player.Playing += (_, _) => { Interlocked.Exchange(ref applyGain, 1); wake(); };
                player.LengthChanged += (_, e) => Interlocked.Exchange(ref length, e.Length);
                player.EndReached += (_, _) => { Interlocked.Exchange(ref successful, 1); Interlocked.Exchange(ref finished, 1); wake(); };
                player.EncounteredError += (_, _) =>
                {
                    Interlocked.Exchange(ref failed, 1);
                    Interlocked.Exchange(ref finished, 1);
                    wake();
                };
                player.Stopped += (_, _) => { Interlocked.Exchange(ref finished, 1); wake(); };
            }
            catch { Dispose(); throw; }
        }

        public void Start()
        {
            elapsed.Start();
            AudioOutputSelection.Apply(player!, ref outputId);
            player!.Volume = volume;
            if (!player.Play())
            {
                Interlocked.Exchange(ref failed, 1);
                Interlocked.Exchange(ref finished, 1);
                throw new InvalidOperationException("Audio playback could not start.");
            }
        }

        public void SetGain(double gain)
        {
            volume = PlayerVolume(gain);
            Interlocked.Exchange(ref applyGain, 1);
            Refresh();
        }

        // PulseAudio applies a volume reliably only after Playing, away from its callback.
        public void Refresh()
        {
            if (Volatile.Read(ref failed) != 0) throw new InvalidOperationException("Audio playback failed.");
            AudioOutputSelection.Apply(player!, ref outputId);
            var position = player!.Time;
            Interlocked.Exchange(ref sourceMilliseconds, Math.Max(0, position));
            Volatile.Write(ref isPlaying, player.State == VLCState.Playing ? 1 : 0);
            var now = elapsed.ElapsedMilliseconds;
            if (position != lastPosition) { lastPosition = position; lastProgressAt = now; }
            else if (elapsed.IsRunning && !IsFinished && now - lastProgressAt > 15_000)
                throw new InvalidOperationException("Audio playback stopped making progress.");
            allowance.Observe(position, now, player.State == VLCState.Playing || WasSuccessful);
            if (!IsFinished && Interlocked.Exchange(ref applyGain, 0) != 0) player.Volume = volume;
        }
        public void Stop() { if (Interlocked.Exchange(ref finished, 1) == 0) player?.Stop(); }
        public void Dispose()
        {
            foreach (var resource in new IDisposable?[] { player, media, input, local })
                try { resource?.Dispose(); } catch { }
            player = null;
        }
    }
}
