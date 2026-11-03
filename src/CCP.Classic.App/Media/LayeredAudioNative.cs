using System.Buffers;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace CCP.Classic.App.Media;

internal interface ILayeredAudioBackend : IDisposable
{
    ILayeredAudioDecoder OpenDecoder(LayeredPcmTrack track, CancellationToken token, Action changed);
    ILayeredAudioOutput OpenOutput(LayeredMixStream stream, TimeProvider clock);
}
internal interface ILayeredAudioDecoder : IDisposable
{
    bool Finished { get; }
    void Poll(bool paused);
}
internal interface ILayeredAudioOutput : IDisposable
{
    bool IsPlaying { get; }
    double PositionSeconds { get; }
    void Poll(bool paused);
    void Mute();
}

// One virtual context for silent decoders, one real output context/player per graph.
// VLC3 amem supports S16N only. Paired rate4/176400Hz preserves normalized44100Hz samples.
internal sealed class LayeredAudioNative(bool silentOutput = false) : ILayeredAudioBackend
{
    private LibVLC? decoders, output;
    private static string[] DecoderOptions => ["--quiet", "--no-video", "--no-spu", "--no-media-library", "--no-lua",
        "--no-metadata-network-access", "--no-sub-autodetect-file", "--no-audio-time-stretch", "--avcodec-threads=1", "--aout=adummy"];
    public ILayeredAudioDecoder OpenDecoder(LayeredPcmTrack track, CancellationToken token, Action changed)
    {
        NativeAudioDevice.EnsureInitialized(); decoders ??= new(DecoderOptions);
        return new Decoder(decoders, track, token, changed);
    }
    public ILayeredAudioOutput OpenOutput(LayeredMixStream stream, TimeProvider clock)
    {
        NativeAudioDevice.EnsureInitialized();
        output ??= new(silentOutput ? DecoderOptions : NativeAudioDevice.Options(OperatingSystem.IsWindows())
            .Concat(new[] { "--avcodec-threads=1", "--no-audio-time-stretch" }).ToArray());
        return new Output(output, stream, clock, silentOutput);
    }
    public void Dispose() { output?.Dispose(); decoders?.Dispose(); }

    private sealed class Decoder : ILayeredAudioDecoder
    {
        private readonly LayeredPcmTrack track;
        private readonly CancellationToken token;
        private readonly Action changed;
        private LocalAudioInput? local;
        private StreamMediaInput? input;
        private LibVLCSharp.Shared.Media? media;
        private MediaPlayer? player;
        private int finished, disposed;
        private bool paused;
        private long lastData = TimeProvider.System.GetTimestamp();
        public bool Finished => Volatile.Read(ref finished) != 0;
        internal Decoder(LibVLC vlc, LayeredPcmTrack track, CancellationToken token, Action changed)
        {
            this.track = track; this.token = token; this.changed = changed;
            try
            {
                local = new(track.Path); input = new(local.Stream); media = new(vlc, input);
                media.AddOption(":demux=" + local.Demux + ",none"); media.AddOption(":no-video"); media.AddOption(":rate=4");
                media.AddOption(":no-metadata-network-access"); media.SubItemAdded += (_, _) => End(false);
                player = new(media); player.SetAudioCallbacks(Receive, null, null, null, null);
                player.SetAudioFormat("S16N", 176400, 2);
                player.SetVolumeCallback((_, _, _) => { }); player.Volume = 0;
                player.EndReached += (_, _) => End(true); player.EncounteredError += (_, _) => End(false);
                if (player.SetRate(4) != 0 || !player.Play()) throw new IOException("Audio layer decoding could not start");
            }
            catch { Dispose(); throw; }
        }
        private void Receive(nint _, nint pointer, uint frames, long pts)
        {
            if (token.IsCancellationRequested || Finished || Volatile.Read(ref disposed) != 0) return;
            if (frames > 176400) { End(false); return; }
            var length = checked((int)frames * 2); var samples = ArrayPool<short>.Shared.Rent(length);
            try
            {
                Marshal.Copy(pointer, samples, 0, length);
                if (!track.Append(samples.AsSpan(0, length))) End(false);
                Interlocked.Exchange(ref lastData, TimeProvider.System.GetTimestamp());
            }
            catch (Exception error) when (error is not OutOfMemoryException) { End(false); }
            finally { ArrayPool<short>.Shared.Return(samples); }
        }
        private void End(bool success)
        {
            if (Interlocked.Exchange(ref finished, 1) != 0) return;
            track.Finish(success && !token.IsCancellationRequested); changed();
        }
        public void Poll(bool pause)
        {
            if (Finished || player is null) return;
            if (!paused && TimeProvider.System.GetElapsedTime(Interlocked.Read(ref lastData)).TotalSeconds > 15)
            { End(false); return; }
            if (paused == pause) return;
            player.SetPause(pause); paused = pause;
            if (!pause) Interlocked.Exchange(ref lastData, TimeProvider.System.GetTimestamp());
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (!Finished) End(false);
            foreach (var resource in new IDisposable?[] { player, media, input, local })
                try { resource?.Dispose(); } catch (Exception error) when (error is not OutOfMemoryException) { }
        }
    }
    private sealed class Output : ILayeredAudioOutput
    {
        private readonly LayeredMixStream stream;
        private readonly TimeProvider clock;
        private readonly StreamMediaInput? input;
        private readonly LibVLCSharp.Shared.Media? media;
        private readonly MediaPlayer? player;
        private string? device;
        private long position = -1, observed;
        private int failed;
        private bool paused;
        private readonly System.Diagnostics.Stopwatch progress = new();
        private long progressAt;
        public bool IsPlaying => Volatile.Read(ref failed) == 0 && !paused && player?.State == VLCState.Playing;
        public double PositionSeconds
        {
            get
            {
                var extrapolate = IsPlaying ? Math.Clamp(clock.GetElapsedTime(observed).TotalSeconds, 0, .12) : 0;
                return Math.Clamp(Math.Max(0, position) / 1000d + extrapolate, 0, stream.ProducedFrames / 44100d);
            }
        }
        internal Output(LibVLC vlc, LayeredMixStream stream, TimeProvider clock, bool silent)
        {
            this.stream = stream; this.clock = clock;
            try
            {
            input = new(stream); media = new(vlc, input); media.AddOption(":demux=wav,none"); media.AddOption(":file-caching=0"); player = new(media);
            if (silent)
            {
                player.SetAudioCallbacks((_, _, _, _) => { }, null, null, null, null); player.SetAudioFormat("S16N", 44100, 2);
                player.SetVolumeCallback((_, _, _) => { });
            }
            player.EncounteredError += (_, _) => Interlocked.Exchange(ref failed, 1);
            player.EndReached += (_, _) => Interlocked.Exchange(ref failed, 1);
            AudioOutputSelection.Apply(player, ref device); player.Volume = 100;
            if (player.SetRate(1) != 0 || !player.Play()) throw new IOException("Layered audio output could not start");
            observed = clock.GetTimestamp(); progress.Start();
            }
            catch { Dispose(); throw; }
        }
        public void Poll(bool pause)
        {
            if (Volatile.Read(ref failed) != 0) throw new IOException("Layered audio output is unavailable");
            AudioOutputSelection.Apply(player!, ref device);
            if (paused != pause) { player!.SetPause(pause); paused = pause; if (pause) progress.Stop(); else progress.Start(); }
            var time = player!.Time;
            if (time != position) { position = time; observed = clock.GetTimestamp(); progressAt = progress.ElapsedMilliseconds; }
            else if (!paused && progress.ElapsedMilliseconds - progressAt > 15000) throw new IOException("Layered audio output stopped making progress");
        }
        public void Mute() { if (player is not null) player.Volume = 0; }
        public void Dispose()
        {
            foreach (var resource in new IDisposable?[] { player, media, input })
                try { resource?.Dispose(); } catch (Exception error) when (error is not OutOfMemoryException) { }
        }
    }
}
