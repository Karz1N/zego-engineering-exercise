using Zego.Crawler.Cli.Arguments;
using Zego.Crawler.Crawling;
using Zego.Crawler.Http;

namespace Zego.Crawler.Cli.Execution;

public static class CliApplication
{
    public const string Usage = "Usage: crawler <http(s)://url> [--concurrency N] [--max-pages N] [--max-response-bytes N] [--timeout-seconds N]";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        try
        {
            return await RunCoreAsync(args, output, error, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await TryWriteErrorAsync(error, "Crawl cancelled; output is partial.");
            return 130;
        }
        catch (IOException)
        {
            await TryWriteErrorAsync(error, "Output could not be written; crawl stopped.");
            return 1;
        }
    }

    private static async Task<int> RunCoreAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token)
    {
        if (args is ["--help"] or ["-h"])
        {
            await output.WriteLineAsync(Usage.AsMemory(), token);
            return 0;
        }

        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message.AsMemory(), token);
            await error.WriteLineAsync(Usage.AsMemory(), token);
            return 2;
        }

        using var fetcher = new HttpPageFetcher(command.Options);
        var crawler = new WebCrawler(fetcher, command.Options);
        CrawlSummary summary = await crawler.RunAsync(command.StartUrl, async (page, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync($"PAGE {page.Url.AbsoluteUri}".AsMemory(), cancellationToken);
            await output.WriteLineAsync($"  STATUS {page.Outcome}{(page.StatusCode is { } status ? $" ({(int)status})" : string.Empty)}".AsMemory(), cancellationToken);
            foreach (Uri link in page.Links)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteLineAsync($"  LINK {link.AbsoluteUri}".AsMemory(), cancellationToken);
            }

            await output.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken);
            if (page.Error is not null)
            {
                await error.WriteLineAsync($"{page.Url.AbsoluteUri}: {page.Error}".AsMemory(), cancellationToken);
            }
        }, token);

        await error.WriteLineAsync($"SUMMARY scheduled={summary.ScheduledPages} html={summary.SuccessfulPages} redirects={summary.RedirectedPages} skipped={summary.SkippedPages} failed={summary.FailedPages} pageLimitReached={summary.PageLimitReached}".AsMemory(), token);
        return summary.IsComplete ? 0 : 1;
    }

    private static async Task TryWriteErrorAsync(TextWriter error, string message)
    {
        try
        {
            await error.WriteLineAsync(message);
        }
        catch (IOException)
        {
            // The diagnostic channel may itself be closed; preserve the intended exit code.
        }
    }
}
