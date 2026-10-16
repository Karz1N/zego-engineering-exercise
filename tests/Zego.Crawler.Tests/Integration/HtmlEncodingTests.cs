using System.Text;
using Microsoft.AspNetCore.Http;
using Zego.Crawler.Crawling;
using Zego.Crawler.Http;
using Zego.Crawler.Tests.TestSupport;

namespace Zego.Crawler.Tests.Integration;

public sealed class HtmlEncodingTests
{
    [Fact]
    public async Task Utf8PageWithoutEncodingDeclarationPreservesLink()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("<a href='/café'>link</a>"));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult page = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.EndsWith("/caf%C3%A9", Assert.Single(page.Links).AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<meta charset='iso-8859-1'>")]
    [InlineData("<meta http-equiv='Content-Type' content='text/html; charset=iso-8859-1'>")]
    public async Task HtmlEncodingDeclarationPreservesExactLink(string declaration)
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.Body.WriteAsync(Encoding.Latin1.GetBytes($"<html><head>{declaration}</head><body><a href='/café'>link</a></body></html>"));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult page = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Success, page.Outcome);
        Assert.EndsWith("/caf%C3%A9", Assert.Single(page.Links).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpCharsetTakesPrecedenceOverConflictingMeta()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html; charset=iso-8859-1";
            await context.Response.Body.WriteAsync(Encoding.Latin1.GetBytes("<meta charset='utf-8'><a href='/café'>link</a>"));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult page = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.EndsWith("/caf%C3%A9", Assert.Single(page.Links).AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public async Task BomTakesPrecedenceOverConflictingHttpCharset(string encodingName)
    {
        Encoding encoding = Encoding.GetEncoding(encodingName);
        byte[] body = encoding.GetPreamble().Concat(encoding.GetBytes("<a href='/café'>link</a>")).ToArray();
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html; charset=iso-8859-1";
            await context.Response.Body.WriteAsync(body);
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult page = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.EndsWith("/caf%C3%A9", Assert.Single(page.Links).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Windows1252HttpCharsetIsSupported()
    {
        Encoding encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html; charset=windows-1252";
            await context.Response.Body.WriteAsync(encoding.GetBytes("<a href='/€'>link</a>"));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult page = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Success, page.Outcome);
        Assert.EndsWith("/%E2%82%AC", Assert.Single(page.Links).AbsoluteUri, StringComparison.Ordinal);
    }
}
