using System.Globalization;
using Zego.Crawler.Crawling;
using Zego.Crawler.Urls;

namespace Zego.Crawler.Cli.Arguments;

public sealed record CommandLine(Uri StartUrl, CrawlOptions Options)
{
    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? startUrl) || !UrlPolicy.IsWebUrl(startUrl))
        {
            throw new ArgumentException("Provide an absolute HTTP(S) URL without credentials as the first argument.");
        }

        var options = new CrawlOptions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!seen.Add(name))
            {
                throw new ArgumentException($"Duplicate option: {name}.");
            }

            if (index + 1 == args.Length || !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                throw new ArgumentException($"Option {name} requires a positive integer.");
            }

            options = name switch
            {
                "--concurrency" => options with { MaxConcurrency = value },
                "--max-pages" => options with { MaxPages = value },
                "--max-response-bytes" => options with { MaxResponseBytes = value },
                "--timeout-seconds" => options with { RequestTimeout = TimeSpan.FromSeconds(value) },
                _ => throw new ArgumentException($"Unknown option: {name}.")
            };
        }

        options.Validate();
        return new CommandLine(startUrl, options);
    }
}
