using System.Security.Cryptography;
using System.Text.Json;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AnalysisBudgetTests
{
    [Theory]
    [InlineData(100UL, 26UL, true)]
    [InlineData(100UL, 25UL, false)]
    [InlineData(100UL, 24UL, false)]
    [InlineData(0UL, 0UL, false)]
    [InlineData(100UL, 101UL, false)]
    public void AnalysisRequiresSystemUsageStrictlyBelowSeventyFivePercent(ulong total, ulong available, bool expected)
        => Assert.Equal(expected, AnalysisProcessBudget.HasHeadroom(total, available));

    [Fact]
    public void AffinityIsASubsetOfAllowedProcessorsAndIncludesAtMostSixteen()
    {
        foreach (var allowed in new nuint[] { 0, 1, 0b10100100, nuint.MaxValue })
        {
            var actual = AnalysisProcessBudget.LimitedAffinity(allowed);
            Assert.Equal((nuint)0, actual & ~allowed);
            Assert.Equal(Math.Min(16, System.Numerics.BitOperations.PopCount((ulong)allowed)), System.Numerics.BitOperations.PopCount((ulong)actual));
        }
    }
    [Fact]
    public void LinuxCountsAvailableCacheAndFailsClosedWhenMemoryMetricsAreMissing()
    {
        Assert.True(AnalysisProcessBudget.LinuxHeadroom(["MemTotal: 1000 kB", "MemFree: 1 kB", "MemAvailable: 300 kB"]));
        Assert.False(AnalysisProcessBudget.LinuxHeadroom(["MemTotal: 1000 kB", "MemFree: 999 kB"]));
        Assert.False(AnalysisProcessBudget.LinuxHeadroom(["MemTotal: 1000 kB", "MemAvailable: 250 kB"]));
    }
    [Fact]
    public void PackagedHelperChecksEveryListedFileAndAllowsUpdatedCompatibleHashes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-helper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "ffmpeg.exe"), [1, 2]); File.WriteAllBytes(Path.Combine(folder, "avcodec.dll"), [3, 4]);
            WriteManifest(); Assert.Equal(Path.Combine(folder, "ffmpeg.exe"), AnalysisHelper.VerifyBundle(folder, "win-x64", "ffmpeg.exe", TestContext.Current.CancellationToken));
            File.WriteAllBytes(Path.Combine(folder, "avcodec.dll"), [4, 3]);
            Assert.Throws<IOException>(() => AnalysisHelper.VerifyBundle(folder, "win-x64", "ffmpeg.exe", TestContext.Current.CancellationToken));
            WriteManifest(); Assert.Equal(Path.Combine(folder, "ffmpeg.exe"), AnalysisHelper.VerifyBundle(folder, "win-x64", "ffmpeg.exe", TestContext.Current.CancellationToken));
            File.Delete(Path.Combine(folder, "avcodec.dll"));
            Assert.Throws<FileNotFoundException>(() => AnalysisHelper.VerifyBundle(folder, "win-x64", "ffmpeg.exe", TestContext.Current.CancellationToken));
            void WriteManifest() => File.WriteAllText(Path.Combine(folder, "analysis-helper.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, runtime = "win-x64", files = new[] { "ffmpeg.exe", "avcodec.dll" }.Select(name => new
                { name, size = new FileInfo(Path.Combine(folder, name)).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, name)))).ToLowerInvariant() })
            }));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData("../ffmpeg.exe")]
    [InlineData("nested/ffmpeg.exe")]
    [InlineData("file:ffmpeg.exe")]
    public void HelperManifestCannotChooseAnExternalExecutable(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-helper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "analysis-helper.json"), JsonSerializer.Serialize(new
            { schemaVersion = 1, runtime = "win-x64", files = new[] { new { name, size = 0, sha256 = new string('0', 64) } } }));
            Assert.Throws<IOException>(() => AnalysisHelper.VerifyBundle(folder, "win-x64", "ffmpeg.exe", TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(folder, true); }
    }
}
