using System.Collections.Concurrent;
using Zego.Crawler.Crawling;

namespace Zego.Crawler.Tests.TestSupport;

internal sealed class TestPageFetcher(Func<Uri, CancellationToken, Task<PageResult>> fetch) : IPageFetcher
{
    private int inFlight;
    private int peak;
    public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
    public int PeakConcurrency => Volatile.Read(ref peak);
    public int ActiveRequests => Volatile.Read(ref inFlight);

    public async Task<PageResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        Calls.AddOrUpdate(url.AbsoluteUri, 1, (_, count) => count + 1);
        int current = Interlocked.Increment(ref inFlight);
        int observed;
        do
        {
            observed = Volatile.Read(ref peak);
        } while (current > observed && Interlocked.CompareExchange(ref peak, current, observed) != observed);

        try
        {
            return await fetch(url, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    public static PageResult Page(Uri url, params string[] links) =>
        new(url, PageOutcome.Success, links.Select(link => new Uri(url, link)).ToArray());

    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
