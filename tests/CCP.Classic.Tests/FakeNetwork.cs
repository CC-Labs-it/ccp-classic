using System.Net;
using System.Text;

namespace CCP.Classic.Tests;

/// <summary>One request as the handler saw it, copied before the sender disposes it.</summary>
public sealed record SeenRequest(string Method, Uri? Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

/// <summary>Stands in for the network: records every request and answers from a script. Tests never go online.</summary>
public sealed class FakeNetwork(Func<SeenRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<SeenRequest> seen = [];
    public IReadOnlyList<SeenRequest> Requests { get { lock (seen) return seen.ToArray(); } }

    public static FakeNetwork Answer(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    public static FakeNetwork Unreachable() => new(_ => throw new HttpRequestException("offline"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var copy = new SeenRequest(request.Method.Method, request.RequestUri, headers, body);
        lock (seen) seen.Add(copy);
        return respond(copy);
    }
}
