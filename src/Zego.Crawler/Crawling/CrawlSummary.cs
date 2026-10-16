namespace Zego.Crawler.Crawling;

public sealed record CrawlSummary(
    int ScheduledPages,
    int SuccessfulPages,
    int RedirectedPages,
    int SkippedPages,
    int FailedPages,
    bool PageLimitReached)
{
    public bool IsComplete => FailedPages == 0 && !PageLimitReached;
}
