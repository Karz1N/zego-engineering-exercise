using System.Text;
using AngleSharp.Dom;
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
        return Extract(document, pageUrl, cancellationToken);
    }

    public async Task<IReadOnlyList<Uri>> ExtractAsync(Stream html, Uri pageUrl, Encoding? encoding, CancellationToken cancellationToken)
    {
        // Let the HTML parser sniff BOM/meta declarations before converting bytes into characters.
        // Streaming mode retains only a bounded source window; the resulting DOM is still in memory.
        using var document = await new HtmlParser().ParseDocumentAsync(html, HtmlStreamSourceMode.Streaming, encoding, cancellationToken);
        return Extract(document, pageUrl, cancellationToken);
    }

    private static IReadOnlyList<Uri> Extract(IDocument document, Uri pageUrl, CancellationToken cancellationToken)
    {
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
