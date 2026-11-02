using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BedrockLan.NetherNet;

/// <summary>Server info returned by <c>GET /v1/join</c>.</summary>
public sealed record HttpJoinInfo(string Name, int Protocol, string Version, string Level, int Players, int MaxPlayers, int GameType);

/// <summary>
/// Client for NetherNet HTTP signaling (Bedrock 26.x dedicated servers / Geyser):
/// <c>GET {base}/v1/join</c> for status, <c>POST {base}/v1/join/{networkId}</c> with an SDP offer for the answer.
/// </summary>
public sealed class HttpSignalingClient : IDisposable
{
    private readonly HttpClient _http;

    public HttpSignalingClient(Uri baseUri, bool allowInvalidCertificate = false, TimeSpan? timeout = null)
    {
        BaseUri = baseUri;
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (allowInvalidCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
    }

    public Uri BaseUri { get; }

    public static Uri BuildBaseUri(string host, int port, bool https) =>
        new UriBuilder(https ? "https" : "http", host, port).Uri;

    public async Task<HttpJoinInfo?> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(new Uri(BaseUri, "/v1/join"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null; // not a NetherNet server
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new HttpJoinInfo(
            GetString(root, "name"),
            GetInt(root, "protocol"),
            GetString(root, "version"),
            GetString(root, "level"),
            GetInt(root, "players"),
            GetInt(root, "maxPlayers"),
            GetInt(root, "gameType"));
    }

    /// <summary>Posts a complete SDP offer and returns the complete SDP answer.</summary>
    public async Task<string> JoinAsync(string networkId, string offerSdp, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(BaseUri, "/v1/join/" + Uri.EscapeDataString(networkId));
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(offerSdp));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");
        using var response = await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Signaling POST returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}", null, response.StatusCode);
        }

        return body;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(value.GetString(), out var s) => s,
            _ => 0,
        };
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "…";

    public void Dispose() => _http.Dispose();
}
