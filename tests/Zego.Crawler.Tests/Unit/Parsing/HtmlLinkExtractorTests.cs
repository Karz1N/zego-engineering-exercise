using Zego.Crawler.Parsing;
using Zego.Crawler.Urls;

namespace Zego.Crawler.Tests.Unit.Parsing;

public sealed class HtmlLinkExtractorTests
{
    private readonly HtmlLinkExtractor extractor = new();

    [Fact]
    public async Task ResolvesHyperlinksDeduplicatesAndKeepsExternalLinks()
    {
        const string html = """
            <a href="../next">relative</a>
            <a href="/root">root</a><a href="/root">duplicate</a>
            <a href="?q=1&amp;x=2">query</a><a href="#part">fragment</a>
            <a href="//external.test/page">external</a>
            <area href="/map">
            <a href="mailto:someone@example.com">mail</a><a href="javascript:void(0)">script</a>
            <a href="https://user:secret@example.com/private">credentials</a>
            <img src="/image.png"><script src="/app.js"></script>
            """;

        IReadOnlyList<Uri> links = await extractor.ExtractAsync(html, new Uri("https://example.com/dir/page"), CancellationToken.None);

        Assert.Equal(new[]
        {
            "https://example.com/dir/page#part",
            "https://example.com/dir/page?q=1&x=2",
            "https://example.com/map",
            "https://example.com/next",
            "https://example.com/root",
            "https://external.test/page"
        }, links.Select(url => url.AbsoluteUri));
    }

    [Fact]
    public async Task FirstBaseElementControlsResolutionWithoutChangingVisitScope()
    {
        const string html = "<base href='https://external.test/dir/'><base href='/ignored/'><a href='next'>next</a>";
        IReadOnlyList<Uri> links = await extractor.ExtractAsync(html, new Uri("https://example.com/page"), CancellationToken.None);
        Uri link = Assert.Single(links);
        Assert.Equal("https://external.test/dir/next", link.AbsoluteUri);
        Assert.False(new UrlPolicy(new Uri("https://example.com")).CanVisit(link));
    }

    [Fact]
    public async Task InvalidBaseFallsBackToPageAndMalformedHtmlIsParsed()
    {
        IReadOnlyList<Uri> links = await extractor.ExtractAsync("<base href='javascript:bad'><a href='next'><b>unfinished", new Uri("https://example.com/dir/page"), CancellationToken.None);
        Assert.Equal("https://example.com/dir/next", Assert.Single(links).AbsoluteUri);
    }

    [Fact]
    public async Task EmptyHyperlinkResolvesToCurrentPage()
    {
        IReadOnlyList<Uri> links = await extractor.ExtractAsync("<a href=''>self</a><a>missing</a>", new Uri("https://example.com/page"), CancellationToken.None);
        Assert.Equal("https://example.com/page", Assert.Single(links).AbsoluteUri);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractor.ExtractAsync("<a href='/x'>x</a>", new Uri("https://example.com"), cancelled.Token));
    }
}
