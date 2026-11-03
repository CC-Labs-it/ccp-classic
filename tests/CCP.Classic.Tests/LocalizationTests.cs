using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] ExpectedCodes =
        ["de", "en", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN"];

    private static byte[] ResourceBytes(string code)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var suffix = $"Localization.Languages.{code}.json";
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static Dictionary<string, string> ResourceStrings(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ResourceBytes(code))!;

    [Fact]
    public void AllTenLanguagesAreCompleteAndActuallyLoad()
    {
        Assert.Equal(ExpectedCodes, LocalizationService.Languages.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal));
        var english = ResourceStrings("en");
        Assert.True(english.Count >= 100);
        foreach (var code in ExpectedCodes)
        {
            var translated = ResourceStrings(code);
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            var localization = new LocalizationService(code);
            Assert.Equal(code, localization.Language);
            foreach (var entry in translated)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{code}/{entry.Key} is empty");
                Assert.Equal(entry.Value, localization[entry.Key]);
            }
        }
    }

    [Fact]
    public void TranslationResourcesHaveNoBomOrProhibitedChromePunctuation()
    {
        foreach (var code in ExpectedCodes)
        {
            var bytes = ResourceBytes(code);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{code} contains a UTF-8 BOM");
            foreach (var entry in ResourceStrings(code))
            {
                Assert.False(entry.Value.Any(x => x is '\u2013' or '\u2014' or '!' or '\uFF01'),
                    $"{code}/{entry.Key} contains prohibited punctuation");
                Assert.DoesNotContain("bambi", entry.Value.ToLowerInvariant());
            }
            Assert.Equal("CC labs Srls", ResourceStrings(code)["ByCredit"]);
        }
        var italian = ResourceStrings("it");
        Assert.DoesNotContain(italian.Values, x => x.Contains("pronto/a", StringComparison.OrdinalIgnoreCase)
            || x.Contains("pronta/o", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownLanguageFallsBackAndChangesNotifyOnce()
    {
        var localization = new LocalizationService("unsupported");
        Assert.Equal("en", localization.Language);
        Assert.Equal("START", localization["Start"]);
        Assert.Equal("MissingKey", localization["MissingKey"]);
        var notifications = 0;
        localization.Changed += () => notifications++;
        localization.SetLanguage("it");
        Assert.Equal("AVVIA", localization["Start"]);
        localization.SetLanguage("IT");
        Assert.Equal(1, notifications);
        localization.SetLanguage("unsupported");
        Assert.Equal(2, notifications);
        Assert.Equal("en", localization.Language);
    }
}
