using Zego.Crawler.Cli.Arguments;
using Zego.Crawler.Crawling;
using Zego.Crawler.Http;

namespace Zego.Crawler.Cli.Execution;

public static class CliApplication
{
    public const string Usage = "Usage: crawler <http(s)://url> [--concurrency N] [--max-pages N] [--max-response-bytes N] [--timeout-seconds N]";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        if (args is ["--help"] or ["-h"])
        {
            await output.WriteLineAsync(Usage);
            return 0;
        }

        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message);
            await error.WriteLineAsync(Usage);
            return 2;
        }

        try
        {
            using var fetcher = new HttpPageFetcher(command.Options);
            var crawler = new WebCrawler(fetcher, command.Options);
            CrawlSummary summary = await crawler.RunAsync(command.StartUrl, async (page, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteLineAsync($"PAGE {page.Url.AbsoluteUri}");
                await output.WriteLineAsync($"  STATUS {page.Outcome}{(page.StatusCode is { } status ? $" ({(int)status})" : string.Empty)}");
                foreach (Uri link in page.Links)
                {
                    await output.WriteLineAsync($"  LINK {link.AbsoluteUri}");
                }

                await output.WriteLineAsync();
                if (page.Error is not null)
                {
                    await error.WriteLineAsync($"{page.Url.AbsoluteUri}: {page.Error}");
                }
            }, token);

            await error.WriteLineAsync($"SUMMARY scheduled={summary.ScheduledPages} html={summary.SuccessfulPages} redirects={summary.RedirectedPages} skipped={summary.SkippedPages} failed={summary.FailedPages} pageLimitReached={summary.PageLimitReached}");
            return summary.IsComplete ? 0 : 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await error.WriteLineAsync("Crawl cancelled; output is partial.");
            return 130;
        }
        catch (IOException)
        {
            await error.WriteLineAsync("Output could not be written; crawl stopped.");
            return 1;
        }
    }
}
