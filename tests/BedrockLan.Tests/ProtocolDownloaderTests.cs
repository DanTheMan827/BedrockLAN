// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace BedrockLan.Tests;

/// <summary>HTTP caching for published protocol configs: fresh copies aren't requested, unchanged ones aren't re-downloaded.</summary>
public class ProtocolDownloaderTests
{
    private const string Url = "https://example.com/protocol-1.jsonc";

    [Fact]
    public async Task FirstDownload_ParsesAndKeepsCachingHeaders()
    {
        var handler = new FakeHandler(_ => Ok(etag: "\"v1\"", maxAge: TimeSpan.FromMinutes(10), lastModified: new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        var result = await ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, null, revalidate: false);

        Assert.Equal(ProtocolDownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(ProtocolConfig.Default.ConfigVersion, result.Config!.ConfigVersion);
        Assert.Equal("\"v1\"", result.Cache.ETag);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), result.Cache.LastModified);
        Assert.InRange(result.Cache.FreshUntil!.Value, DateTimeOffset.UtcNow.AddMinutes(9), DateTimeOffset.UtcNow.AddMinutes(11));
        Assert.Empty(handler.Requests.Single().Headers.IfNoneMatch);
    }

    [Fact]
    public async Task FreshCopy_MakesNoRequest()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("no request expected"));
        var cache = new ProtocolCacheInfo(Url, "\"v1\"", null, DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, cache, revalidate: false);

        Assert.Equal(ProtocolDownloadOutcome.StillFresh, result.Outcome);
        Assert.Null(result.Config);
        Assert.Same(cache, result.Cache);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task StaleCopy_SendsValidatorsAndAccepts304()
    {
        var lastModified = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMinutes(10) };
            return response;
        });
        var cache = new ProtocolCacheInfo(Url, "\"v1\"", lastModified, DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, cache, revalidate: false);

        Assert.Equal(ProtocolDownloadOutcome.NotModified, result.Outcome);
        Assert.Null(result.Config);
        Assert.Equal("\"v1\"", result.Cache.ETag); // kept
        Assert.True(result.Cache.FreshUntil > DateTimeOffset.UtcNow); // freshness renewed
        var request = handler.Requests.Single();
        Assert.Equal("\"v1\"", request.Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(lastModified, request.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Revalidate_AsksEvenWhileFresh()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var cache = new ProtocolCacheInfo(Url, "\"v1\"", null, DateTimeOffset.UtcNow.AddHours(1));

        var result = await ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, cache, revalidate: true);

        Assert.Equal(ProtocolDownloadOutcome.NotModified, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CacheForAnotherUrl_IsIgnored()
    {
        var handler = new FakeHandler(_ => Ok());
        var cache = new ProtocolCacheInfo("https://example.com/protocol-0.jsonc", "\"v1\"", null, DateTimeOffset.UtcNow.AddHours(1));

        var result = await ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, cache, revalidate: false);

        Assert.Equal(ProtocolDownloadOutcome.Downloaded, result.Outcome);
        Assert.Empty(handler.Requests.Single().Headers.IfNoneMatch);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]          // only 200 carries a config
    [InlineData(HttpStatusCode.NotModified)]        // 304 without having asked conditionally isn't an answer
    public async Task OtherStatuses_Throw(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, null, revalidate: false));
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task MalformedFile_Throws()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"formatVersion\": 1, ") });
        await Assert.ThrowsAsync<ProtocolConfigException>(() => ProtocolDownloader.DownloadAsync(new HttpClient(handler), Url, null, revalidate: false));
    }

    [Fact]
    public void NoCache_MeansAlwaysRevalidate_AndExpiresIsUsedWithoutMaxAge()
    {
        var now = DateTimeOffset.UtcNow;
        var noCache = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        noCache.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, MaxAge = TimeSpan.FromHours(1) };
        Assert.Null(ProtocolDownloader.FreshUntil(noCache, now));

        var expires = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        expires.Headers.Date = now;
        expires.Content.Headers.Expires = now.AddMinutes(30);
        Assert.Equal(now.AddMinutes(30), ProtocolDownloader.FreshUntil(expires, now));

        var aged = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        aged.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMinutes(10) };
        aged.Headers.Age = TimeSpan.FromMinutes(4); // already 4 minutes old in a shared cache (a CDN)
        Assert.Equal(now.AddMinutes(6), ProtocolDownloader.FreshUntil(aged, now));
    }

    [Fact]
    public void CacheInfo_RoundTripsThroughJson()
    {
        var info = new ProtocolCacheInfo(Url, "W/\"abc\"", new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 5, 12, 10, 0, TimeSpan.Zero));
        Assert.Equal(info, ProtocolCacheInfo.FromJson(info.ToJson()));
        Assert.Null(ProtocolCacheInfo.FromJson("not json"));
        Assert.Null(ProtocolCacheInfo.FromJson(null));
    }

    private static HttpResponseMessage Ok(string? etag = null, TimeSpan? maxAge = null, DateTimeOffset? lastModified = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ProtocolConfig.DefaultText, Encoding.UTF8, "text/plain"),
        };
        if (etag is not null)
        {
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        }

        if (maxAge is not null)
        {
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = maxAge };
        }

        response.Content.Headers.LastModified = lastModified;
        return response;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}
