using System.Security.Cryptography;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class StarterPackTests
{
    [Fact]
    public void BundledStarterHasTheVerifiedDefaultImageAndFiveNeutralRecordings()
    {
        var pack = new StarterPack();
        Assert.Equal(new[] { "FOCUS", "BREATHE", "RELAX", "LISTEN", "DEEPER" }, pack.Phrases.Select(p => p.Text));
        var expected = new Dictionary<string, string>
        {
            ["focus.mp3"] = "65c5a795c34b9e245d48089e8b541f89b84145b41cf6fc7106c70d710eab279c",
            ["breathe.mp3"] = "3e82d0ebdf1accb6d1d817177d3007d2e00499b575b33bf5ed6428f334f8b636",
            ["relax.mp3"] = "bdc1fe71f15951890ff9ce12fbfd0a505958fdff2e119a0cf6f147841b2bc158",
            ["listen.mp3"] = "5ebb42e79e5dac1612b0141e29c5743f911871b0fee7b5a3351c827c69ecdf4a",
            ["deeper.mp3"] = "7d25687bb44a4fc58dcf43c11b74ff2236146a1b2dee8f18e6406f8dad9614c2"
        };
        foreach (var phrase in pack.Phrases)
        {
            Assert.NotNull(phrase.AudioPath);
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(phrase.AudioPath))).ToLowerInvariant();
            Assert.Equal(expected[Path.GetFileName(phrase.AudioPath)], actual);
        }
        var image = Assert.Single(pack.Images);
        Assert.Equal("59b776a8c7ebb814091e5e310858af3186e14418452f86cd747323a78e9743f1",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image))).ToLowerInvariant());
    }

    [Fact]
    public void MissingMediaLeavesTheApprovedWordsAsSilentText()
    {
        var absent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var pack = new StarterPack(absent);
        Assert.Equal(5, pack.Phrases.Count);
        Assert.All(pack.Phrases, p => Assert.Null(p.AudioPath));
        Assert.Empty(pack.Images);
    }
}
