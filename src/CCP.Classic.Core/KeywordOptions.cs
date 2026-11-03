namespace CCP.Classic.Core;

public enum KeywordMatch { PlainText, Regex }
public enum KeywordAppScope { Everywhere, ExceptListed, OnlyListed }
public enum KeywordActionKind { Visual, Audio, Haptic, Xp }
public enum KeywordVisual { Flash, Subliminal, ExactText, Pink, Spiral, MindWipe, Bubbles, LockCard, BrainDrain, Whisper, Video }

public sealed class KeywordAction
{
    public KeywordActionKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public KeywordVisual Visual { get; set; }
    public string? AudioPath { get; set; }
    public double Gain { get; set; } = .8;
    public int PlayCount { get; set; } = 1;
    public int DelayMs { get; set; } = 200;
    public bool DuckAudio { get; set; } = true;
    public int Xp { get; set; } = 10;
    public void Normalize()
    {
        if (!Enum.IsDefined(Kind)) Kind = KeywordActionKind.Visual;
        if (!Enum.IsDefined(Visual)) Visual = KeywordVisual.Flash;
        AudioPath = SubliminalOptions.LocalAudio(AudioPath);
        Gain = double.IsFinite(Gain) ? Math.Clamp(Gain, 0, 1) : .8;
        PlayCount = Math.Clamp(PlayCount, 1, 5); DelayMs = Math.Clamp(DelayMs, 0, 5000); Xp = Math.Clamp(Xp, 0, 100);
    }
}

public sealed class KeywordDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Keyword { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public KeywordMatch Match { get; set; }
    public int CooldownSeconds { get; set; } = 30;
    public KeywordAppScope AppScope { get; set; }
    public List<string> Apps { get; set; } = [];
    public List<KeywordAction> Actions { get; set; } = [new()];
    public void Normalize()
    {
        if (!Guid.TryParseExact(Id, "N", out _)) Id = Guid.NewGuid().ToString("N");
        Keyword = (Keyword ?? "").Trim(); if (Keyword.Length > 256) Keyword = Keyword[..256];
        if (!Enum.IsDefined(Match)) Match = KeywordMatch.PlainText;
        if (!Enum.IsDefined(AppScope)) AppScope = KeywordAppScope.Everywhere;
        CooldownSeconds = Math.Clamp(CooldownSeconds, 1, 3600);
        Apps = (Apps ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(64).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Actions = (Actions ?? []).Where(a => a is not null).Take(16).ToList();
        foreach (var action in Actions) action.Normalize();
    }
}

public sealed class KeywordOptions
{
    public int BufferTimeoutMs { get; set; } = 3000;
    public int GlobalCooldownSeconds { get; set; } = 10;
    public int SameWordCooldownSeconds { get; set; } = 15;
    public double SessionXpMultiplier { get; set; } = 1.5;
    public bool IgnoreOwnFocus { get; set; }
    public KeywordAppScope AppScope { get; set; }
    public List<string> Apps { get; set; } = [];
    public bool LoopProtection { get; set; } = true;
    public int LoopProtectionMs { get; set; } = 5000;
    public List<KeywordDefinition> Triggers { get; set; } =
    [
        new() { Keyword = "Relax", Actions = [new() { Visual = KeywordVisual.Flash }] },
        new() { Keyword = "Focus", Actions = [new() { Visual = KeywordVisual.Whisper }] },
        new() { Keyword = "Deeper", Actions = [new() { Visual = KeywordVisual.Spiral }] }
    ];
    public void Normalize()
    {
        BufferTimeoutMs = Math.Clamp(BufferTimeoutMs, 1000, 10000);
        GlobalCooldownSeconds = Math.Clamp(GlobalCooldownSeconds, 1, 3600);
        SameWordCooldownSeconds = Math.Clamp(SameWordCooldownSeconds, 1, 3600);
        SessionXpMultiplier = double.IsFinite(SessionXpMultiplier) ? Math.Clamp(SessionXpMultiplier, 1, 3) : 1.5;
        LoopProtectionMs = Math.Clamp(LoopProtectionMs, 500, 30000);
        if (!Enum.IsDefined(AppScope)) AppScope = KeywordAppScope.Everywhere;
        Apps = (Apps ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(64).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Triggers = (Triggers ?? []).Where(t => t is not null).Take(128).ToList();
        foreach (var trigger in Triggers) trigger.Normalize();
        var ids = new HashSet<string>();
        foreach (var trigger in Triggers) if (!ids.Add(trigger.Id)) { trigger.Id = Guid.NewGuid().ToString("N"); ids.Add(trigger.Id); }
    }
    public int ImportPhrases(IEnumerable<SubliminalPhrase> phrases)
    {
        var existing = Triggers.Select(t => t.Keyword).ToHashSet(StringComparer.OrdinalIgnoreCase); var count = 0;
        foreach (var phrase in phrases.Take(500))
        {
            var word = phrase.Text.Trim();
            if (Triggers.Count >= 128 || word.Length == 0 || !existing.Add(word)) continue;
            var trigger = new KeywordDefinition { Keyword = word, Actions = [new() { Visual = KeywordVisual.Subliminal },
                new() { Kind = KeywordActionKind.Haptic, Gain = .5 }, new() { Kind = KeywordActionKind.Xp, Xp = 10 }] };
            if (SubliminalOptions.LocalAudio(phrase.AudioPath) is { } audio)
                trigger.Actions.Insert(0, new() { Kind = KeywordActionKind.Audio, AudioPath = audio });
            trigger.Normalize(); Triggers.Add(trigger); count++;
        }
        return count;
    }
}
