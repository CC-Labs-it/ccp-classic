using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioFormatsTests
{
    internal static byte[] AsfHeader => Convert.FromHexString("3026B2758E66CF11A6D900AA0062CE6C");
    private static readonly Dictionary<string, (byte[] Header, string Demux)> Containers = new()
    {
        [".mp3"] = ("ID3"u8.ToArray(), "mp3"), [".wav"] = ("RIFF0000WAVE"u8.ToArray(), "wav"),
        [".flac"] = ("fLaC"u8.ToArray(), "flac"), [".ogg"] = ("OggS"u8.ToArray(), "ogg"),
        [".m4a"] = ("0000ftypM4A "u8.ToArray(), "mp4"), [".aac"] = ([0xff, 0xf1], "aac"),
        [".opus"] = ("OggS"u8.ToArray(), "ogg"), [".wma"] = (AsfHeader, "asf")
    };

    [Fact]
    public void DiscoveryClipSettingsPlaybackAndPickerPatternsShareAllEightFormats()
    {
        using var fixture = new Folder();
        foreach (var (extension, container) in Containers)
        {
            var path = fixture.Write("clip" + extension.ToUpperInvariant(), container.Header);
            Assert.True(LocalAudioFormats.SupportsPath(path)); Assert.Equal(path, SubliminalOptions.LocalAudio(path));
            var wipe = new MindWipeOptions { AudioPath = path }; wipe.Normalize(); Assert.Equal(path, wipe.AudioPath);
            using var input = new LocalAudioInput(path); Assert.Equal(container.Demux, input.Demux); Assert.Equal(0, input.Stream.Position);
        }
        var expected = Directory.GetFiles(fixture.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, MediaLibrary.Scan(audioDirectories: [fixture.Path], cancellationToken: TestContext.Current.CancellationToken).Audio);
        Assert.Equal(Containers.Keys.Order(), LocalAudioFormats.Extensions.Order());
        Assert.Equal(Containers.Keys.Select(extension => "*" + extension).Order(), LocalAudioFormats.Patterns.Order());
    }

    [Fact]
    public void WmaRequiresEveryByteOfTheAsfHeaderObjectGuid()
    {
        Assert.Equal("asf", LocalAudioInput.DetectDemux(AsfHeader, ".wma"));
        Assert.Equal("asf", FfmpegEnvelopeDecoder.Demux(AsfHeader, ".wma"));
        for (var length = 0; length < 16; length++)
        {
            Assert.Null(LocalAudioInput.DetectDemux(AsfHeader.AsSpan(0, length), ".wma"));
            Assert.Null(FfmpegEnvelopeDecoder.Demux(AsfHeader.AsSpan(0, length), ".wma"));
        }
        for (var index = 0; index < 16; index++)
        {
            var wrong = AsfHeader; wrong[index] ^= 1;
            Assert.Null(LocalAudioInput.DetectDemux(wrong, ".wma")); Assert.Null(FfmpegEnvelopeDecoder.Demux(wrong, ".wma"));
        }
        var args = FfmpegEnvelopeDecoder.StartInfo(Path.GetFullPath("ffmpeg.exe"), Path.GetFullPath("clip.wma"), "asf").ArgumentList.ToArray();
        Assert.Equal("asf", args[Array.IndexOf(args, "-f") + 1]);
        Assert.Equal("file,pipe", args[Array.IndexOf(args, "-protocol_whitelist") + 1]);
    }

    [Theory]
    [InlineData("<ASX version=\"3.0\"><ENTRY><REF HREF=\"https://example.invalid/media\"/></ENTRY></ASX>")]
    [InlineData("#EXTM3U\nhttps://example.invalid/media")]
    [InlineData("[playlist]\nFile1=https://example.invalid/media")]
    public void APlaylistRenamedWmaCannotEnterEitherNativeDecoder(string text)
    {
        using var fixture = new Folder(); var path = fixture.Write("disguised.wma", System.Text.Encoding.UTF8.GetBytes(text));
        Assert.Throws<IOException>(() => new LocalAudioInput(path));
        Assert.Null(FfmpegEnvelopeDecoder.Demux(File.ReadAllBytes(path), ".wma"));
    }

    [Fact]
    public void TruncatedHeaderAndNonlocalPathsFailBeforeNativePlayback()
    {
        using var fixture = new Folder();
        for (var count = 0; count < 16; count++)
        {
            var path = fixture.Write($"truncated-{count}.wma", AsfHeader[..count]);
            Assert.Throws<IOException>(() => new LocalAudioInput(path));
        }
        foreach (var path in new[] { "https://example.invalid/a.wma", "file:///tmp/a.wma", @"\\server\share\a.wma",
                     "//server/share/a.wma", "relative.wma", Path.Combine(fixture.Path, "a.wma:stream") })
        { Assert.Null(LocalAudioInput.LocalPath(path)); Assert.Null(SubliminalOptions.LocalAudio(path)); }
        foreach (var extension in new[] { ".asx", ".m3u", ".pls" })
        {
            var path = fixture.Write("unsupported" + extension, AsfHeader);
            Assert.False(LocalAudioFormats.SupportsPath(path)); Assert.Null(LocalAudioInput.LocalPath(path));
        }
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CCP Classic audio formats", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public string Write(string name, byte[] data) { var path = System.IO.Path.Combine(Path, name); File.WriteAllBytes(path, data); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
