namespace Zego.Crawler.Urls;

/// <summary>Defines crawl scope and fetch identity without merging potentially distinct resources.</summary>
public sealed class UrlPolicy
{
    private readonly string host;

    public UrlPolicy(Uri startUrl)
    {
        if (!IsWebUrl(startUrl))
        {
            throw new ArgumentException("An absolute HTTP(S) URL without credentials is required.", nameof(startUrl));
        }

        host = startUrl.IdnHost;
    }

    public bool CanVisit(Uri url) => IsWebUrl(url)
        && string.Equals(host, url.IdnHost, StringComparison.OrdinalIgnoreCase);

    public static bool IsWebUrl(Uri url) => url.IsAbsoluteUri
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(url.Host)
        && string.IsNullOrEmpty(url.UserInfo);

    public static Uri Normalize(Uri url)
    {
        if (!IsWebUrl(url))
        {
            throw new ArgumentException("An absolute HTTP(S) URL without credentials is required.", nameof(url));
        }

        return new UriBuilder(url) { Host = url.IdnHost, Fragment = string.Empty }.Uri;
    }
}
