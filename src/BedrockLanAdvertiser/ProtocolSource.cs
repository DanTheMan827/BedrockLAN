using BedrockLan;

namespace BedrockLanAdvertiser;

/// <summary>Loads a protocol config from a file or an http(s) URL ("builtin" = the one built into the library).</summary>
internal static class ProtocolSource
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The value that means "use the built-in config, never download".</summary>
    public const string BuiltIn = "builtin";

    public static bool IsBuiltIn(string source) => string.Equals(source.Trim(), BuiltIn, StringComparison.OrdinalIgnoreCase);

    public static bool IsUrl(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Exceptions that mean "couldn't load or parse it" (as opposed to bugs).</summary>
    public static bool IsLoadFailure(Exception ex) =>
        ex is ProtocolConfigException or IOException or HttpRequestException or UnauthorizedAccessException
            || (ex is TaskCanceledException && ex.InnerException is TimeoutException);

    /// <summary>A local config file.</summary>
    public static async Task<ProtocolConfig> LoadFileAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (text.Length > ProtocolDownloader.MaxBytes)
        {
            throw new IOException($"the file is larger than {ProtocolDownloader.MaxBytes / 1024} KB");
        }

        return ProtocolConfig.Parse(text);
    }

    /// <summary>
    /// Checks a URL, following HTTP caching: nothing is requested while <paramref name="cache"/> is fresh, and an
    /// unchanged file is answered with 304 instead of being downloaded again.
    /// </summary>
    public static Task<ProtocolDownloadResult> CheckUrlAsync(string url, ProtocolCacheInfo? cache, CancellationToken cancellationToken) =>
        ProtocolDownloader.DownloadAsync(Http, url, cache, revalidate: false, cancellationToken);
}
