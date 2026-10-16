using Zego.Crawler.Crawling;

namespace Zego.Crawler.Tests.Unit.Crawling;

public sealed class CrawlOptionsTests
{
    [Fact]
    public void InvalidResourceLimitsAreRejectedBeforeWorkStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlOptions { MaxConcurrency = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlOptions { MaxPages = -1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlOptions { MaxResponseBytes = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CrawlOptions { RequestTimeout = TimeSpan.Zero }.Validate());
    }
}
