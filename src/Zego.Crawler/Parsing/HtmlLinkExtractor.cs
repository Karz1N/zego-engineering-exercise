using AngleSharp.Html.Parser;
using Zego.Crawler.Urls;

namespace Zego.Crawler.Parsing;

/// <summary>Extracts HTTP(S) hyperlinks from server HTML without executing scripts or loading resources.</summary>
public sealed class HtmlLinkExtractor
{
    public async Task<IReadOnlyList<Uri>> ExtractAsync(string html, Uri pageUrl, CancellationToken cancellationToken)
    {
        // Each parse owns its DOM and parser; concurrent fetches do not share mutable parser state.
        using var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
        Uri baseUrl = pageUrl;
        string? baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        if (Uri.TryCreate(pageUrl, baseHref, out Uri? declaredBase) && UrlPolicy.IsWebUrl(declaredBase))
        {
            baseUrl = declaredBase;
        }

        var links = new SortedDictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var element in document.QuerySelectorAll("a[href], area[href]"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? href = element.GetAttribute("href")?.Trim();
            if (Uri.TryCreate(baseUrl, href, out Uri? link) && UrlPolicy.IsWebUrl(link))
            {
                links.TryAdd(link.AbsoluteUri, link);
            }
        }

        return links.Values.ToArray();
    }
}
