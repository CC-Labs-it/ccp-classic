using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CCP.Classic.App.Media;

internal sealed class NativeAudioDevice : IAudioDevice
{
    private static readonly object InitializationGate = new();
    private static bool initialized, resolverInstalled;
    private readonly LibVLC vlc;

    public NativeAudioDevice()
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
        vlc = new LibVLC(Options(OperatingSystem.IsWindows()));
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
        private readonly LibVLCSharp.Shared.Media media;
        private readonly MediaPlayer player;
        private readonly int volume;
        private int finished, applyGain, failed;
        public bool IsFinished => Volatile.Read(ref finished) != 0;

        public Voice(LibVLC vlc, string path, double gain, Action wake)
        {
            media = new LibVLCSharp.Shared.Media(vlc, path, FromType.FromPath);
            media.AddOption(":no-video");
            media.AddOption(":no-metadata-network-access");
            try { player = new MediaPlayer(media); }
            catch { media.Dispose(); throw; }
            volume = PlayerVolume(gain);
            player.Playing += (_, _) => { Interlocked.Exchange(ref applyGain, 1); wake(); };
            player.EndReached += (_, _) => { Interlocked.Exchange(ref finished, 1); wake(); };
            player.EncounteredError += (_, _) =>
            {
                Interlocked.Exchange(ref failed, 1);
                Interlocked.Exchange(ref finished, 1);
                wake();
            };
            player.Stopped += (_, _) => { Interlocked.Exchange(ref finished, 1); wake(); };
        }

        public void Start()
        {
            player.Volume = volume;
            if (!player.Play())
            {
                Interlocked.Exchange(ref failed, 1);
                Interlocked.Exchange(ref finished, 1);
                throw new InvalidOperationException("Audio playback could not start.");
            }
        }

        // PulseAudio applies a volume reliably only after Playing, away from its callback.
        public void Refresh()
        {
            if (Volatile.Read(ref failed) != 0) throw new InvalidOperationException("Audio playback failed.");
            if (!IsFinished && Interlocked.Exchange(ref applyGain, 0) != 0) player.Volume = volume;
        }
        public void Stop() { if (Interlocked.Exchange(ref finished, 1) == 0) player.Stop(); }
        public void Dispose() { player.Dispose(); media.Dispose(); }
    }
}
