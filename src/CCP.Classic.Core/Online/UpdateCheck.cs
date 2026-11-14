using System.Text.Json;

namespace CCP.Classic.Core.Online;

public enum UpdateStatus { UpToDate, Available, Failed }

public sealed record UpdateResult(UpdateStatus Status, string? Version = null, Uri? Page = null);

/// <summary>
/// One of Classic's two opt-in network features (owner approval 2026-11-09). It runs only when the
/// player turned on "Check for updates on startup" or pressed Check now. It makes ONE plain GET to
/// the public GitHub releases list with a User-Agent naming the app and version, and nothing else:
/// no cookies, no identifiers, no body. It never downloads or runs anything; a newer release only
/// yields its page, which the player may open in the system browser.
/// </summary>
public sealed class UpdateCheck(HttpMessageHandler? handler = null)
{
    // Classic ships betas as GitHub prereleases, and /releases/latest skips those, so the list is read.
    public static readonly Uri ReleasesApi = new("https://api.github.com/repos/CC-Labs-it/ccp-classic/releases?per_page=20");
    public static readonly Uri ReleasesPage = new("https://github.com/CC-Labs-it/ccp-classic/releases");
    private const string PagePrefix = "https://github.com/CC-Labs-it/ccp-classic/releases/";
    private const int MaxResponseBytes = 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The exact request this check sends; tests compare it with what the handler received.</summary>
    public static HttpRequestMessage CreateRequest(string appVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.ParseAdd("CCP-Classic/" + UserAgentVersion(appVersion));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    public async Task<UpdateResult> CheckAsync(string appVersion, CancellationToken cancel = default)
    {
        if (!ClassicVersion.TryParse(appVersion, out var current)) return new(UpdateStatus.Failed);
        try
        {
            using var client = handler is null
                ? new HttpClient(new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 3 })
                : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            using var request = CreateRequest(appVersion);
            using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(UpdateStatus.Failed);
            var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return Pick(json, current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException
            or InvalidOperationException or IOException)
        {
            return new(UpdateStatus.Failed);
        }
    }

    /// <summary>The newest published release above the running version, if any. Drafts never count.</summary>
    public static UpdateResult Pick(string json, ClassicVersion current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return new(UpdateStatus.Failed);
            ClassicVersion? best = null; Uri? page = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object) continue;
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                if (!release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                    || !ClassicVersion.TryParse(tag.GetString(), out var version)) continue;
                if (best is { } known && version.CompareTo(known) <= 0) continue;
                best = version;
                page = release.TryGetProperty("html_url", out var url) && url.ValueKind == JsonValueKind.String
                    && url.GetString() is { } address && address.StartsWith(PagePrefix, StringComparison.Ordinal)
                    && Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed : ReleasesPage;
            }
            return best is { } newest && newest > current
                ? new(UpdateStatus.Available, newest.ToString(), page)
                : new(UpdateStatus.UpToDate);
        }
        catch (JsonException) { return new(UpdateStatus.Failed); }
    }

    private static string UserAgentVersion(string appVersion) =>
        ClassicVersion.TryParse(appVersion, out var version) ? version.ToString() : "unknown";
}
