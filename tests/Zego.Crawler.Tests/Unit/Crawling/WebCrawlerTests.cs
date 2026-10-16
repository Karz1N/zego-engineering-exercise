using Zego.Crawler.Crawling;
using Zego.Crawler.Tests.TestSupport;

namespace Zego.Crawler.Tests.Unit.Crawling;

public sealed class WebCrawlerTests
{
    private static readonly Uri Start = new("https://example.com/");
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CyclesFragmentsAndRepeatedLinksAreFetchedOnceAndExternalLinksAreRetained()
    {
        var fetcher = new TestPageFetcher((url, _) => Task.FromResult(url.AbsolutePath switch
        {
            "/" => TestPageFetcher.Page(url, "/b#one", "/b#two", "https://external.test/", "https://sub.example.com/"),
            _ => TestPageFetcher.Page(url, "/", "/b")
        }));
        var pages = new List<PageResult>();
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(Start, (page, _) =>
        {
            pages.Add(page);
            return ValueTask.CompletedTask;
        });

        Assert.True(summary.IsComplete);
        Assert.Equal(2, summary.ScheduledPages);
        Assert.Equal(2, pages.Count);
        Assert.All(fetcher.Calls.Values, count => Assert.Equal(1, count));
        Assert.Contains(pages[0].Links, url => url.Host == "external.test");
        Assert.DoesNotContain(fetcher.Calls.Keys, url => url.Contains("external.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryVariantsAndTrailingSlashRemainDistinct()
    {
        var fetcher = new TestPageFetcher((url, _) => Task.FromResult(url == Start
            ? TestPageFetcher.Page(url, "/item", "/item/", "/item?q=1", "/item?q=2", "/item?q=1")
            : TestPageFetcher.Page(url)));
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(Start, Ignore);
        Assert.Equal(5, summary.ScheduledPages);
        Assert.All(fetcher.Calls.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task ConcurrentDiscoveriesShareOneScheduledIdentityAndRespectConcurrency()
    {
        TaskCompletionSource twoStarted = TestPageFetcher.Signal();
        TaskCompletionSource release = TestPageFetcher.Signal();
        int started = 0;
        var fetcher = new TestPageFetcher(async (url, token) =>
        {
            if (url.AbsolutePath == "/") return TestPageFetcher.Page(url, "/a", "/b", "/c");
            if (url.AbsolutePath == "/shared") return TestPageFetcher.Page(url);
            if (Interlocked.Increment(ref started) == 2) twoStarted.SetResult();
            await release.Task.WaitAsync(token);
            return TestPageFetcher.Page(url, "/shared");
        });
        Task<CrawlSummary> run = new WebCrawler(fetcher, new CrawlOptions { MaxConcurrency = 2 }).RunAsync(Start, Ignore);
        await twoStarted.Task.WaitAsync(TestDeadline);
        Assert.False(run.IsCompleted);
        release.SetResult();
        CrawlSummary summary = await run.WaitAsync(TestDeadline);

        Assert.Equal(2, fetcher.PeakConcurrency);
        Assert.Equal(5, summary.SuccessfulPages);
        Assert.Equal(1, fetcher.Calls["https://example.com/shared"]);
    }

    [Fact]
    public async Task EmptyQueueDoesNotFinishWhileFetchCanStillDiscoverLinks()
    {
        TaskCompletionSource started = TestPageFetcher.Signal();
        TaskCompletionSource release = TestPageFetcher.Signal();
        var fetcher = new TestPageFetcher(async (url, token) =>
        {
            if (url == Start)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
                return TestPageFetcher.Page(url, "/later");
            }
            return TestPageFetcher.Page(url);
        });
        Task<CrawlSummary> run = new WebCrawler(fetcher, new CrawlOptions()).RunAsync(Start, Ignore);
        await started.Task.WaitAsync(TestDeadline);
        Assert.False(run.IsCompleted);
        release.SetResult();
        Assert.Equal(2, (await run.WaitAsync(TestDeadline)).SuccessfulPages);
    }

    [Fact]
    public async Task PageLimitBoundsScheduledWorkAndReportsPartialResult()
    {
        var fetcher = new TestPageFetcher((url, _) => Task.FromResult(TestPageFetcher.Page(url, "/a", "/b", "/c")));
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions { MaxPages = 2 }).RunAsync(Start, Ignore);
        Assert.True(summary.PageLimitReached);
        Assert.False(summary.IsComplete);
        Assert.Equal(2, fetcher.Calls.Count);
        Assert.Equal(2, summary.ScheduledPages);
    }

    [Fact]
    public async Task ExactlyReachingLimitIsCompleteWhenNoNewEligibleUrlsExist()
    {
        var fetcher = new TestPageFetcher((url, _) => Task.FromResult(TestPageFetcher.Page(url, "/", "https://external.test/")));
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions { MaxPages = 1 }).RunAsync(Start, Ignore);
        Assert.False(summary.PageLimitReached);
        Assert.True(summary.IsComplete);
    }

    [Fact]
    public async Task FailureDoesNotStopIndependentPages()
    {
        var fetcher = new TestPageFetcher((url, _) => Task.FromResult(url.AbsolutePath switch
        {
            "/" => TestPageFetcher.Page(url, "/broken", "/healthy"),
            "/broken" => new PageResult(url, PageOutcome.Failed, Array.Empty<Uri>(), Error: "HTTP 500."),
            _ => TestPageFetcher.Page(url)
        }));
        CrawlSummary summary = await new WebCrawler(fetcher, new CrawlOptions()).RunAsync(Start, Ignore);
        Assert.Equal(1, summary.FailedPages);
        Assert.Equal(2, summary.SuccessfulPages);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public async Task CancellationDrainsOutstandingRequests()
    {
        TaskCompletionSource started = TestPageFetcher.Signal();
        using var cancellation = new CancellationTokenSource();
        var fetcher = new TestPageFetcher(async (url, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return TestPageFetcher.Page(url);
        });
        Task<CrawlSummary> run = new WebCrawler(fetcher, new CrawlOptions()).RunAsync(Start, Ignore, cancellation.Token);
        await started.Task.WaitAsync(TestDeadline);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestDeadline));
        Assert.Equal(0, fetcher.ActiveRequests);
    }

    [Fact]
    public async Task OutputFailureCancelsAndObservesOtherInFlightRequests()
    {
        TaskCompletionSource otherStarted = TestPageFetcher.Signal();
        var fetcher = new TestPageFetcher(async (url, token) =>
        {
            if (url == Start) return TestPageFetcher.Page(url, "/a", "/b");
            if (url.AbsolutePath == "/a")
            {
                await otherStarted.Task.WaitAsync(token);
                return TestPageFetcher.Page(url);
            }
            otherStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return TestPageFetcher.Page(url);
        });
        Task<CrawlSummary> run = new WebCrawler(fetcher, new CrawlOptions { MaxConcurrency = 2 }).RunAsync(Start, (page, _) =>
        {
            if (page.Url.AbsolutePath == "/a") throw new IOException("Output failed.");
            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TestDeadline));
        Assert.Equal(0, fetcher.ActiveRequests);
    }

    private static ValueTask Ignore(PageResult _, CancellationToken __) => ValueTask.CompletedTask;
}
