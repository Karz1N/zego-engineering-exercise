using Zego.Crawler.Cli.Execution;
using Zego.Crawler.Tests.TestSupport;

namespace Zego.Crawler.Tests.Integration;

public sealed class CliRobustnessTests
{
    [Fact]
    public async Task ClosedErrorWriterReturnsFailureWithoutThrowingAgain()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context => LocalHttpSite.HtmlAsync(context, "<p>done</p>"));
        using var output = new StringWriter();
        using var error = new ThrowingTextWriter();
        Assert.Equal(1, await CliApplication.RunAsync(new[] { site.BaseUrl.AbsoluteUri }, output, error));
    }

    [Fact]
    public async Task ClosedOutputAndErrorWritersReturnFailure()
    {
        using var output = new ThrowingTextWriter();
        using var error = new ThrowingTextWriter();
        Assert.Equal(1, await CliApplication.RunAsync(new[] { "--help" }, output, error));
    }

    [Fact]
    public async Task CancellationDuringPageOutputStopsRemainingLinks()
    {
        await using LocalHttpSite site = await LocalHttpSite.StartAsync(context => LocalHttpSite.HtmlAsync(context,
            "<a href='/a'>a</a><a href='/b'>b</a><a href='/c'>c</a>"));
        using var cancellation = new CancellationTokenSource();
        using var output = new CancellingTextWriter(cancellation);
        using var error = new StringWriter();
        Assert.Equal(130, await CliApplication.RunAsync(new[] { site.BaseUrl.AbsoluteUri }, output, error, cancellation.Token));
        Assert.Equal(1, output.LinksWritten);
    }
}
