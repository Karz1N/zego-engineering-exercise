namespace Zego.Crawler.Crawling;

/// <summary>Fetches one URL. Returns expected transport failures; propagates caller cancellation.</summary>
public interface IPageFetcher
{
    Task<PageResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}
