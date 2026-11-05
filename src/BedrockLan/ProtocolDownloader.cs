using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BedrockLan;

/// <summary>
/// What a previous download said about the published config, so the next check can avoid downloading it again:
/// its validators (ETag / Last-Modified, for a conditional request answered with 304 Not Modified) and how long
/// it stays fresh (Cache-Control max-age / Expires, during which no request is needed at all).
/// </summary>
/// <param name="Url">The URL this applies to (cache info for another URL is ignored).</param>
/// <param name="ETag">The ETag header of the last good download.</param>
/// <param name="LastModified">The Last-Modified header of the last good download.</param>
/// <param name="FreshUntil">Until when the copy is fresh per the server's caching headers (null: always revalidate).</param>
public sealed record ProtocolCacheInfo(string Url, string? ETag, DateTimeOffset? LastModified, DateTimeOffset? FreshUntil)
{
    /// <summary>A small JSON document, to keep next to a cached config.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("url", Url);
            Optional(w, "etag", ETag);
            Optional(w, "lastModified", LastModified?.ToString("O", CultureInfo.InvariantCulture));
            Optional(w, "freshUntil", FreshUntil?.ToString("O", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());

        static void Optional(Utf8JsonWriter w, string name, string? value)
        {
            if (value is not null)
            {
                w.WriteString(name, value);
            }
        }
    }

    /// <summary>Reads <see cref="ToJson"/> output; null if it's missing or unreadable (the next check simply downloads).</summary>
    public static ProtocolCacheInfo? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return new ProtocolCacheInfo(url.GetString()!, Text(root, "etag"), Date(root, "lastModified"), Date(root, "freshUntil"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static DateTimeOffset? Date(JsonElement root, string name) =>
            DateTimeOffset.TryParse(Text(root, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }
}

public enum ProtocolDownloadOutcome
{
    /// <summary>The server sent a config (200) and it's valid: <see cref="ProtocolDownloadResult.Config"/>.</summary>
    Downloaded,

    /// <summary>The server confirmed the cached copy is current (304 Not Modified); nothing was downloaded.</summary>
    NotModified,

    /// <summary>The cached copy is still fresh per the server's caching headers; no request was made.</summary>
    StillFresh,
}

/// <summary>The result of <see cref="ProtocolDownloader.DownloadAsync"/>; keep <see cref="Cache"/> for the next check.</summary>
public sealed record ProtocolDownloadResult(ProtocolDownloadOutcome Outcome, ProtocolConfig? Config, ProtocolCacheInfo Cache);

/// <summary>
/// Downloads a published protocol config, following HTTP caching: no request while the last copy is fresh
/// (Cache-Control max-age, or Expires), then a conditional request (If-None-Match / If-Modified-Since) that the
/// server answers with 304 Not Modified when nothing changed. A full download happens only when the file did.
/// </summary>
public static class ProtocolDownloader
{
    /// <summary>Largest config accepted (the built-in one is about 20 KB).</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>
    /// Checks <paramref name="url"/> for a newer config.
    /// </summary>
    /// <param name="http">The client to use.</param>
    /// <param name="url">The config's URL.</param>
    /// <param name="cache">
    /// What the last good download said (null, or info for a different URL: download unconditionally). Pass it only
    /// while the copy it describes is the one in use, so a 304 really means "you have it".
    /// </param>
    /// <param name="revalidate">Ask the server even if the copy is still fresh (e.g. a "check now" button).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="HttpRequestException">The server answered with anything but 200 or 304, or couldn't be reached.</exception>
    /// <exception cref="ProtocolConfigException">The downloaded file isn't a valid config.</exception>
    /// <exception cref="IOException">The file is larger than <see cref="MaxBytes"/>.</exception>
    public static async Task<ProtocolDownloadResult> DownloadAsync(HttpClient http, string url, ProtocolCacheInfo? cache, bool revalidate, CancellationToken cancellationToken = default)
    {
        if (cache is not null && cache.Url != url)
        {
            cache = null;
        }

        var now = DateTimeOffset.UtcNow;
        if (!revalidate && cache?.FreshUntil is { } freshUntil && freshUntil > now)
        {
            return new ProtocolDownloadResult(ProtocolDownloadOutcome.StillFresh, null, cache);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cache is not null)
        {
            if (cache.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            if (cache.LastModified is { } lastModified)
            {
                request.Headers.IfModifiedSince = lastModified;
            }
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cache is not null)
        {
            // Same file: keep the validators (the server may send fresher ones) and take the new freshness.
            var etag = response.Headers.ETag?.ToString() ?? cache.ETag;
            var lastModified = response.Content.Headers.LastModified ?? cache.LastModified;
            return new ProtocolDownloadResult(ProtocolDownloadOutcome.NotModified, null,
                new ProtocolCacheInfo(url, etag, lastModified, FreshUntil(response, now)));
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"the server answered {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new IOException($"the file is larger than {MaxBytes / 1024} KB");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > MaxBytes)
        {
            throw new IOException($"the file is larger than {MaxBytes / 1024} KB");
        }

        var config = ProtocolConfig.Parse(text);
        var info = new ProtocolCacheInfo(url, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, FreshUntil(response, now));
        return new ProtocolDownloadResult(ProtocolDownloadOutcome.Downloaded, config, info);
    }

    /// <summary>
    /// How long the response stays fresh (RFC 9111): no-cache / no-store mean "always revalidate"; otherwise
    /// max-age (less the Age the response already has), or else Expires (relative to the server's Date).
    /// </summary>
    internal static DateTimeOffset? FreshUntil(HttpResponseMessage response, DateTimeOffset now)
    {
        var cacheControl = response.Headers.CacheControl;
        if (cacheControl is { NoCache: true } or { NoStore: true })
        {
            return null;
        }

        if (cacheControl?.MaxAge is { } maxAge)
        {
            var age = response.Headers.Age ?? TimeSpan.Zero;
            var remaining = maxAge - age;
            return remaining > TimeSpan.Zero ? now + remaining : null;
        }

        if (response.Content.Headers.Expires is { } expires)
        {
            var serverNow = response.Headers.Date ?? now;
            var remaining = expires - serverNow;
            return remaining > TimeSpan.Zero ? now + remaining : null;
        }

        return null;
    }
}
