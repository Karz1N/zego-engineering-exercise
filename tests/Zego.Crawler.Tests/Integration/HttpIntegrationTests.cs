using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Zego.Crawler.Cli.Execution;
using Zego.Crawler.Crawling;
using Zego.Crawler.Http;
using Zego.Crawler.Tests.TestSupport;

namespace Zego.Crawler.Tests.Integration;

public sealed class HttpIntegrationTests
{
    [Fact]
    public async Task ConnectionAbortedByServerIsReportedAsTransportFailure()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context =>
        {
            context.Abort();
            return Task.CompletedTask;
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult result = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Failed, result.Outcome);
        Assert.Equal("HTTP transport failed.", result.Error);
    }

    [Fact]
    public async Task CliCrawlsLocalGraphAndWritesCompleteNonInterleavedPageBlocks()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context => LocalHttpSite.HtmlAsync(context,
            context.Request.Path == "/" ? "<a href='/a'>a</a><a href='/b'>b</a><a href='/a#part'>duplicate</a><a href='https://external.test/'>external</a>"
            : "<a href='/'>cycle</a><a href='/shared'>shared</a>"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await CliApplication.RunAsync(new[] { site.BaseUrl.AbsoluteUri, "--concurrency", "2" }, output, error);

        Assert.Equal(0, code);
        Assert.Equal(4, site.Hits.Count);
        Assert.All(site.Hits.Values, count => Assert.Equal(1, count));
        string[] blocks = output.ToString().Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, blocks.Length);
        Assert.All(blocks, block =>
        {
            Assert.StartsWith("PAGE ", block, StringComparison.Ordinal);
            Assert.Equal(1, block.Split("PAGE ", StringSplitOptions.None).Length - 1);
            Assert.Contains("  STATUS Success (200)", block, StringComparison.Ordinal);
        });
        Assert.Contains("LINK https://external.test/", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("failed=0", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectsAreScheduledOnceAndExternalRedirectIsNeverFollowed()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            if (context.Request.Path == "/")
            {
                await LocalHttpSite.HtmlAsync(context, "<a href='/alias'>alias</a><a href='/target'>target</a><a href='/external'>external</a>");
            }
            else if (context.Request.Path == "/alias") context.Response.Redirect("/target");
            else if (context.Request.Path == "/external")
            {
                context.Response.Redirect($"http://localhost:{context.Request.Host.Port}/forbidden");
            }
            else await LocalHttpSite.HtmlAsync(context, "<a href='/'>root</a>");
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        var pages = new List<PageResult>();
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(site.BaseUrl, (page, _) =>
        {
            pages.Add(page);
            return ValueTask.CompletedTask;
        });

        Assert.True(summary.IsComplete);
        Assert.Equal(2, summary.RedirectedPages);
        Assert.Equal(1, site.Hits["/target"]);
        Assert.False(site.Hits.ContainsKey("/forbidden"));
        Assert.Contains(pages.SelectMany(page => page.Links), url => url.Host == "localhost");
    }

    [Fact]
    public async Task RedirectCycleTerminatesWithEachUrlRequestedOnce()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context =>
        {
            context.Response.Redirect(context.Request.Path == "/" ? "/loop" : "/");
            return Task.CompletedTask;
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(site.BaseUrl, Ignore);
        Assert.Equal(2, summary.RedirectedPages);
        Assert.All(site.Hits.Values, count => Assert.Equal(1, count));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task RedirectWithoutLocationIsReportedAsFailure(int code)
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context =>
        {
            context.Response.StatusCode = code;
            return Task.CompletedTask;
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult result = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Failed, result.Outcome);
        Assert.Equal((HttpStatusCode)code, result.StatusCode);
    }

    [Fact]
    public async Task HttpErrorsAreIsolatedAndNonHtmlIsSkipped()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            switch (context.Request.Path.Value)
            {
                case "/": await LocalHttpSite.HtmlAsync(context, "<a href='/error'>error</a><a href='/binary'>binary</a><a href='/healthy'>healthy</a>"); break;
                case "/error": context.Response.StatusCode = 500; break;
                case "/binary": context.Response.ContentType = "application/octet-stream"; await context.Response.WriteAsync("binary"); break;
                default: await LocalHttpSite.HtmlAsync(context, "<p>healthy</p>"); break;
            }
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(site.BaseUrl, Ignore);
        Assert.Equal(2, summary.SuccessfulPages);
        Assert.Equal(1, summary.FailedPages);
        Assert.Equal(1, summary.SkippedPages);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public async Task StreamedResponseWithoutContentLengthIsStillBounded()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.StartAsync();
            await context.Response.WriteAsync(new string('x', 1025));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions { MaxResponseBytes = 1024 });
        PageResult result = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Failed, result.Outcome);
        Assert.Contains("exceeds 1024", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentLengthCannotBypassResponseLimit()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            context.Response.ContentLength = 1025;
            await context.Response.WriteAsync(new string('x', 1025));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions { MaxResponseBytes = 1024 });
        Assert.Equal(PageOutcome.Failed, (await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task CompressionCannotBypassDecodedResponseLimit()
    {
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(new string('x', 4096)));
        }
        byte[] bytes = compressed.ToArray();
        Assert.True(bytes.Length < 1024);
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            context.Response.Headers.ContentEncoding = "gzip";
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes);
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions { MaxResponseBytes = 1024 });
        Assert.Equal(PageOutcome.Failed, (await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task DeclaredCharsetIsRespected()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html; charset=iso-8859-1";
            await context.Response.Body.WriteAsync(Encoding.Latin1.GetBytes("<a href='/café'>coffee</a>"));
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        PageResult result = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None);
        Assert.Equal(PageOutcome.Success, result.Outcome);
        Assert.EndsWith("/caf%C3%A9", Assert.Single(result.Links).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCharsetIsReportedAsFailure()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html; charset=invalid-encoding";
            await context.Response.WriteAsync("<p>page</p>");
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        Assert.Equal(PageOutcome.Failed, (await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task TimeoutCoversResponseBodyNotOnlyHeaders()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.StartAsync();
            await context.Response.Body.FlushAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        using var fetcher = new HttpPageFetcher(new CrawlOptions { RequestTimeout = TimeSpan.FromMilliseconds(250) });
        PageResult result = await fetcher.FetchAsync(site.BaseUrl, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PageOutcome.Failed, result.Outcome);
        Assert.Equal("Request timed out.", result.Error);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBecomingTimeout()
    {
        TaskCompletionSource entered = TestPageFetcher.Signal();
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource();
        using var fetcher = new HttpPageFetcher(new CrawlOptions());
        Task<PageResult> fetch = fetcher.FetchAsync(site.BaseUrl, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CliMarksFailurePageLimitAndCancellationWithDistinctExitCodes()
    {
        TaskCompletionSource entered = TestPageFetcher.Signal();
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(async context =>
        {
            if (context.Request.Path == "/error") context.Response.StatusCode = 404;
            else if (context.Request.Path == "/slow")
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            else await LocalHttpSite.HtmlAsync(context, "<a href='/next'>next</a>");
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await CliApplication.RunAsync(new[] { new Uri(site.BaseUrl, "/error").AbsoluteUri }, output, error));
        Assert.Equal(1, await CliApplication.RunAsync(new[] { site.BaseUrl.AbsoluteUri, "--max-pages", "1" }, output, error));
        Assert.Contains("pageLimitReached=True", error.ToString(), StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource();
        Task<int> run = CliApplication.RunAsync(new[] { new Uri(site.BaseUrl, "/slow").AbsoluteUri }, output, error, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        Assert.Equal(130, await run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static ValueTask Ignore(PageResult _, CancellationToken __) => ValueTask.CompletedTask;
}
