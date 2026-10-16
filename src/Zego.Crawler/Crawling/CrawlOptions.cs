namespace Zego.Crawler.Crawling;

public sealed record CrawlOptions
{
    public int MaxConcurrency { get; init; } = 4;
    public int MaxPages { get; init; } = 10_000;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxResponseBytes);
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "Timeout must be positive and supported by CancellationTokenSource.");
        }
    }
}
