using Zego.Crawler.Urls;

namespace Zego.Crawler.Crawling;

/// <summary>Streams results while keeping all scheduling state owned by one coordinator.</summary>
public sealed class WebCrawler(IPageFetcher fetcher, CrawlOptions options)
{
    public async Task<CrawlSummary> RunAsync(
        Uri startUrl,
        Func<PageResult, CancellationToken, ValueTask> onPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onPage);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var policy = new UrlPolicy(startUrl);
        var pending = new Queue<Uri>();
        var scheduled = new HashSet<string>(StringComparer.Ordinal);
        var active = new List<Task<PageResult>>();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool pageLimitReached = false;
        int successful = 0, redirected = 0, skipped = 0, failed = 0;

        Schedule(startUrl);
        try
        {
            while (pending.Count > 0 || active.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (pending.Count > 0 && active.Count < options.MaxConcurrency)
                {
                    active.Add(fetcher.FetchAsync(pending.Dequeue(), lifetime.Token));
                }

                Task<PageResult> completed = await Task.WhenAny(active).WaitAsync(cancellationToken);
                active.Remove(completed);
                PageResult page = await completed;
                foreach (Uri link in page.Links)
                {
                    Schedule(link);
                }

                switch (page.Outcome)
                {
                    case PageOutcome.Success: successful++; break;
                    case PageOutcome.Redirected: redirected++; break;
                    case PageOutcome.Skipped: skipped++; break;
                    case PageOutcome.Failed: failed++; break;
                }

                await onPage(page, cancellationToken);
            }

            return new CrawlSummary(scheduled.Count, successful, redirected, skipped, failed, pageLimitReached);
        }
        finally
        {
            // Observe every outstanding task, including when cancellation or the output callback fails.
            await lifetime.CancelAsync();
            try
            {
                await Task.WhenAll(active);
            }
            catch
            {
                // Preserve the original exception; remaining fetches have all been observed.
            }
        }

        void Schedule(Uri url)
        {
            if (!policy.CanVisit(url))
            {
                return;
            }

            Uri normalized = UrlPolicy.Normalize(url);
            if (scheduled.Contains(normalized.AbsoluteUri))
            {
                return;
            }

            if (scheduled.Count == options.MaxPages)
            {
                pageLimitReached = true;
                return;
            }

            scheduled.Add(normalized.AbsoluteUri);
            pending.Enqueue(normalized);
        }
    }
}
