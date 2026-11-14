using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CCP.Classic.Core.Online;

/// <summary>What one report will carry. Built once, shown in the preview, and sent as is.</summary>
public sealed record BugReportDraft(string AppVersion, string Os, string Description, string Contact,
    bool IncludeLog, string Log, ScrubCounts Counts);

public enum BugReportOutcome { Sent, Saved, RateLimited, Failed }

public sealed record BugReportResult(BugReportOutcome Outcome, string? Id = null);

/// <summary>
/// The second opt-in network feature (owner approval 2026-11-09). A report goes to the same CC Labs
/// pipeline as the full CCP app (proxy /bug/upload, which files a GitHub issue and a #bug-alerts post)
/// tagged as Classic. Nothing is collected in the background: the player writes the text, may add a
/// contact line and this run's scrubbed log, reads the exact body in a preview, and presses Send.
/// No account, no identifiers: the body below plus a time stamp, a signature and the User-Agent.
/// </summary>
public static class BugReport
{
    public static readonly Uri Endpoint = new("https://codebambi-proxy.vercel.app/bug/upload");
    public const int MaxDescription = 4000;
    public const int MaxContact = 200;
    public const int MaxLog = 30000;

    // The proxy's anti-noise signature key. It already ships in the public CCP desktop source, so it is
    // not a secret: it only keeps casual junk off the endpoint. Rotating it is a server change for both apps.
    private const string SignatureKey = "067dd64f975e275fd4ba5dd06febaa63f95dcdb93f667ae44716046a30d14b95";

    public static BugReportDraft Create(string appVersion, string os, string description, string contact, bool includeLog, string log)
    {
        var (scrubbed, counts) = includeLog ? ReportScrubber.Scrub(log) : ("", ScrubCounts.None);
        // Newest lines win: the end of a log is where it went wrong.
        if (scrubbed.Length > MaxLog) scrubbed = scrubbed[^MaxLog..];
        return new(Clip(appVersion, 40), Clip(os, 80), Clip((description ?? "").Trim(), MaxDescription),
            Clip(OneLine(contact), MaxContact), includeLog, scrubbed, counts);
    }

    /// <summary>The exact bytes POSTed. Field order is fixed so the preview reads the same every time.</summary>
    public static string Body(BugReportDraft draft) => Write(draft, indented: false);

    /// <summary>The same object as <see cref="Body"/>, indented for reading.</summary>
    public static string Preview(BugReportDraft draft) => Write(draft, indented: true);

    public static string Signature(string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SignatureKey));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body)));
    }

    private static string Write(BugReportDraft draft, bool indented)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = indented,
            // Accents and CJK stay readable in the preview instead of turning into escapes.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            json.WriteStartObject();
            json.WriteStartObject("metadata");
            json.WriteString("app", "classic");
            json.WriteString("app_version", draft.AppVersion);
            json.WriteString("os", draft.Os);
            json.WriteEndObject();
            json.WriteString("description", draft.Description);
            json.WriteString("contact", draft.Contact);
            json.WriteString("steps", "");
            json.WriteString("crash_log", "");
            json.WriteString("app_log", draft.IncludeLog ? draft.Log : "");
            json.WriteStartObject("scrubber_counts");
            json.WriteNumber("paths", draft.Counts.Paths);
            json.WriteNumber("files", draft.Counts.Files);
            json.WriteNumber("emails", draft.Counts.Emails);
            json.WriteNumber("tokens", draft.Counts.Tokens);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Clip(string? value, int max)
    {
        var text = value ?? "";
        return text.Length > max ? text[..max] : text;
    }

    private static string OneLine(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}

/// <summary>Sends one draft. Only called from the preview's Send button.</summary>
public sealed class BugReportSender(HttpMessageHandler? handler = null, TimeProvider? time = null)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public async Task<BugReportResult> SendAsync(BugReportDraft draft, CancellationToken cancel = default)
    {
        var body = BugReport.Body(draft);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var client = handler is null
                ? new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
                : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout;
            client.MaxResponseContentBufferSize = 64 * 1024;
            using var request = new HttpRequestMessage(HttpMethod.Post, BugReport.Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.UserAgent.ParseAdd("CCP-Classic/" + (ClassicVersion.TryParse(draft.AppVersion, out var v) ? v.ToString() : "unknown"));
            request.Headers.Add("X-Bug-Timestamp", timestamp);
            request.Headers.Add("X-Bug-Signature", BugReport.Signature(timestamp, body));
            using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status == 429) return new(BugReportOutcome.RateLimited);
            if (status is not (200 or 202) || ReadId(text) is not { } id) return new(BugReportOutcome.Failed);
            return new(status == 200 ? BugReportOutcome.Sent : BugReportOutcome.Saved, id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException
            or InvalidOperationException or IOException)
        {
            return new(BugReportOutcome.Failed);
        }
    }

    private static string? ReadId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String
                && token.GetString() is { } id && System.Text.RegularExpressions.Regex.IsMatch(id, "^BUG-[A-Z2-9]{10}$") ? id : null;
        }
        catch (JsonException) { return null; }
    }
}
