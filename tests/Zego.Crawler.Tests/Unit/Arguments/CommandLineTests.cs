using Zego.Crawler.Cli.Arguments;
using Zego.Crawler.Cli.Execution;

namespace Zego.Crawler.Tests.Unit.Arguments;

public sealed class CommandLineTests
{
    [Fact]
    public void ParsesOptionsIndependentlyOfCulture()
    {
        CommandLine command = CommandLine.Parse(new[] { "https://example.com", "--concurrency", "3", "--max-pages", "20", "--timeout-seconds", "7", "--max-response-bytes", "1000" });
        Assert.Equal(3, command.Options.MaxConcurrency);
        Assert.Equal(20, command.Options.MaxPages);
        Assert.Equal(1000, command.Options.MaxResponseBytes);
        Assert.Equal(TimeSpan.FromSeconds(7), command.Options.RequestTimeout);
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("not-a-url")]
    public void InvalidSeedIsRejected(string seed)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { seed }));
    }

    [Theory]
    [InlineData("--concurrency", "0")]
    [InlineData("--concurrency", "-1")]
    [InlineData("--concurrency", "1.5")]
    [InlineData("--unknown", "2")]
    [InlineData("--timeout-seconds", "2147483647")]
    public void InvalidOptionIsRejected(string option, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => CommandLine.Parse(new[] { "https://example.com", option, value }));
    }

    [Fact]
    public void MissingValueAndDuplicateOptionAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { "https://example.com", "--concurrency" }));
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { "https://example.com", "--concurrency", "2", "--concurrency", "3" }));
    }

    [Fact]
    public async Task HelpSucceedsAndInvalidInputReturnsUsageError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await CliApplication.RunAsync(new[] { "--help" }, output, error));
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, await CliApplication.RunAsync(Array.Empty<string>(), output, error));
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }
}
