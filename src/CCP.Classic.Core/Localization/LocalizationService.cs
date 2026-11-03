using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CCP.Classic.Core.Localization;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed class LocalizationService
{
    public static IReadOnlyList<LanguageOption> Languages { get; } = Array.AsReadOnly(new[]
    {
        new LanguageOption("en", "English"), new("de", "Deutsch"),
        new("es", "Español"), new("fr", "Français"), new("it", "Italiano"),
        new("ja", "日本語"), new("ko", "한국어"), new("pt-BR", "Português (Brasil)"),
        new("ru", "Русский"), new("zh-CN", "简体中文")
    });

    private readonly Dictionary<string, string> english = ReadLanguage("en");
    private Dictionary<string, string> strings = new();
    public string Language { get; private set; } = "";
    public event Action? Changed;

    public LocalizationService(string language = "en") => SetLanguage(language);

    public string this[string key] => strings.TryGetValue(key, out var text) ? text : key;

    public void SetLanguage(string code)
    {
        var selected = Languages.FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
        if (selected == Language) return;
        var next = selected == "en" ? english : ReadLanguage(selected);
        if (!english.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(next.Keys))
            throw new InvalidDataException($"Incomplete language: {selected}");
        strings = next;
        Language = selected;
        Changed?.Invoke();
    }

    private static Dictionary<string, string> ReadLanguage(string code)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var suffix = $"Localization.Languages.{code}.json";
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Missing language: {code}");
        using var json = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            var value = entry.Value.GetString();
            if (string.IsNullOrWhiteSpace(value) || !result.TryAdd(entry.Name, value))
                throw new InvalidDataException($"Invalid translation: {code}/{entry.Name}");
        }
        return result;
    }
}
